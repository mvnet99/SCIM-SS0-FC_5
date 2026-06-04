using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Domain.Services.Interfaces.Pewo;
using Microsoft.Extensions.Logging;
using Renci.SshNet;
using SendGrid;
using SendGrid.Helpers.Mail;
using System.IO.Compression;
using System.Text.Json;

namespace Domain.Services.Pewo.Steps
{
    // ─────────────────────────────────────────────────────────────────────────
    // Step context passed from PewoWorkerService into each step
    // ─────────────────────────────────────────────────────────────────────────

    public class PewoStepContext
    {
        public Guid RunId { get; init; }
        public string WorkflowCode { get; init; } = string.Empty;
        public bool DryRun { get; init; }
        /// <summary>Step config JSON from WorkflowStepDef.config</summary>
        public string? ConfigJson { get; init; }
        /// <summary>In-memory files streamed by READ_BLOB, available to ZIP step.</summary>
        public Dictionary<string, byte[]> InMemoryFiles { get; } = new();
        /// <summary>Set by ZIP step; used by SFTP step.</summary>
        public string? StagingBlobPath { get; set; }
    }

    public class StepResult
    {
        public bool Success { get; init; }
        public string? Reason { get; init; }
        public string? ArtifactRef { get; init; }

        public static StepResult Ok(string? artifactRef = null) => new() { Success = true, ArtifactRef = artifactRef };
        public static StepResult Fail(string reason) => new() { Success = false, Reason = reason };
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Step 2 — READ_BLOB
    // Streams GM+PRPC .txt files from fc-hold-target into memory. Zero disk I/O.
    // ─────────────────────────────────────────────────────────────────────────

    public class ReadBlobStep
    {
        private readonly BlobServiceClient _blobServiceClient;
        private readonly ILogger<ReadBlobStep> _logger;

        public ReadBlobStep(BlobServiceClient blobServiceClient, ILogger<ReadBlobStep> logger)
        {
            _blobServiceClient = blobServiceClient;
            _logger = logger;
        }

        public async Task<StepResult> ExecuteAsync(PewoStepContext ctx, CancellationToken cancellationToken)
        {
            var cfg = JsonSerializer.Deserialize<ReadBlobConfig>(ctx.ConfigJson ?? "{}") ?? new();

            _logger.LogInformation("[PEWO][READ_BLOB] RunId={RunId} Container={Container}", ctx.RunId, cfg.SourceContainer);

            var container = _blobServiceClient.GetBlobContainerClient(cfg.SourceContainer);
            var filesLoaded = 0;

            foreach (var prefix in new[] { cfg.FilePatternGm, cfg.FilePatternPrpc })
            {
                await foreach (var blobItem in container.GetBlobsAsync(prefix: prefix, cancellationToken: cancellationToken))
                {
                    var blobClient = container.GetBlobClient(blobItem.Name);
                    using var ms = new MemoryStream();
                    await blobClient.DownloadToAsync(ms, cancellationToken);
                    ctx.InMemoryFiles[blobItem.Name] = ms.ToArray();
                    filesLoaded++;
                    _logger.LogInformation("[PEWO][READ_BLOB] Loaded {Name} ({Bytes} bytes)", blobItem.Name, ms.Length);
                }
            }

            if (filesLoaded == 0)
                return StepResult.Fail($"No files found in '{cfg.SourceContainer}' for patterns '{cfg.FilePatternGm}' / '{cfg.FilePatternPrpc}'");

            return StepResult.Ok($"loaded:{filesLoaded}");
        }

        private record ReadBlobConfig
        {
            public string SourceContainer  { get; init; } = "fc-hold-target";
            public string FilePatternGm    { get; init; } = "TAR_ITM_GM_";
            public string FilePatternPrpc  { get; init; } = "TAR_ITM_PRPC_";
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Step 3 — ZIP
    // Zips in-memory files and writes staging/{runId}.zip to pewo-staging Blob.
    // Idempotent: overwrites same runId key.
    // ─────────────────────────────────────────────────────────────────────────

    public class ZipStep
    {
        private readonly BlobServiceClient _blobServiceClient;
        private readonly ILogger<ZipStep> _logger;

        public ZipStep(BlobServiceClient blobServiceClient, ILogger<ZipStep> logger)
        {
            _blobServiceClient = blobServiceClient;
            _logger = logger;
        }

        public async Task<StepResult> ExecuteAsync(PewoStepContext ctx, CancellationToken cancellationToken)
        {
            if (ctx.InMemoryFiles.Count == 0)
                return StepResult.Fail("No in-memory files to zip (READ_BLOB must succeed first)");

            var cfg = JsonSerializer.Deserialize<ZipConfig>(ctx.ConfigJson ?? "{}") ?? new();
            var blobPath = $"staging/{ctx.RunId}.zip";

            _logger.LogInformation("[PEWO][ZIP] RunId={RunId} Files={Count} DryRun={DryRun}", ctx.RunId, ctx.InMemoryFiles.Count, ctx.DryRun);

            using var zipStream = new MemoryStream();
            using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create, leaveOpen: true))
            {
                foreach (var (name, data) in ctx.InMemoryFiles)
                {
                    var entry = archive.CreateEntry(Path.GetFileName(name), CompressionLevel.Optimal);
                    await using var entryStream = entry.Open();
                    await entryStream.WriteAsync(data, cancellationToken);
                }
            }

            zipStream.Position = 0;

            if (!ctx.DryRun)
            {
                var container = _blobServiceClient.GetBlobContainerClient(cfg.StagingContainer);
                var blobClient = container.GetBlobClient(blobPath);
                await blobClient.UploadAsync(zipStream, overwrite: true, cancellationToken);
                _logger.LogInformation("[PEWO][ZIP] Uploaded {Path} ({Bytes} bytes)", blobPath, zipStream.Length);
            }
            else
            {
                _logger.LogInformation("[PEWO][ZIP] DryRun — skipping Blob upload. BlobPath={Path}", blobPath);
            }

            ctx.StagingBlobPath = blobPath;
            return StepResult.Ok(blobPath);
        }

        private record ZipConfig
        {
            public string StagingContainer { get; init; } = "pewo-staging";
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Step 4 — SFTP
    // SSH.NET. Upload temp name → rename final (idempotent: skip if final exists).
    // SSH key loaded from environment (Key Vault mounts as env var in AKS).
    // ─────────────────────────────────────────────────────────────────────────

    public class SftpStep
    {
        private readonly BlobServiceClient _blobServiceClient;
        private readonly ILogger<SftpStep> _logger;

        public SftpStep(BlobServiceClient blobServiceClient, ILogger<SftpStep> logger)
        {
            _blobServiceClient = blobServiceClient;
            _logger = logger;
        }

        public async Task<StepResult> ExecuteAsync(PewoStepContext ctx, CancellationToken cancellationToken)
        {
            var cfg = JsonSerializer.Deserialize<SftpConfig>(ctx.ConfigJson ?? "{}") ?? new();

            if (string.IsNullOrWhiteSpace(ctx.StagingBlobPath))
                return StepResult.Fail("StagingBlobPath not set — ZIP step must succeed first");

            var host       = Environment.GetEnvironmentVariable("SFTPServerHostUrl")     ?? throw new InvalidOperationException("SFTPServerHostUrl not set");
            var portStr    = Environment.GetEnvironmentVariable("SFTPServerPortNumber")   ?? "22";
            var username   = Environment.GetEnvironmentVariable("SFTPServerUsername")     ?? throw new InvalidOperationException("SFTPServerUsername not set");
            var privateKey = Environment.GetEnvironmentVariable("SFTPServerSSHPrivateKey") ?? throw new InvalidOperationException("SFTPServerSSHPrivateKey not set");
            var port       = int.Parse(portStr);

            var fileName     = $"{ctx.RunId}.zip";
            var tempFileName = $"{ctx.RunId}.zip.tmp";
            var finalPath    = $"{cfg.RemotePath}/{fileName}";
            var tempPath     = $"{cfg.RemotePath}/{tempFileName}";

            _logger.LogInformation("[PEWO][SFTP] RunId={RunId} Host={Host} Path={Path} DryRun={DryRun}", ctx.RunId, host, finalPath, ctx.DryRun);

            if (ctx.DryRun)
            {
                _logger.LogInformation("[PEWO][SFTP] DryRun — skipping SFTP upload");
                return StepResult.Ok($"sftp:dryrun:{finalPath}");
            }

            // Download zip from staging blob
            var container  = _blobServiceClient.GetBlobContainerClient("pewo-staging");
            var blobClient = container.GetBlobClient(ctx.StagingBlobPath);
            using var zipStream = new MemoryStream();
            await blobClient.DownloadToAsync(zipStream, cancellationToken);
            zipStream.Position = 0;

            // Load private key
            using var keyStream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(privateKey));
            var keyFile         = new PrivateKeyFile(keyStream);
            var connectionInfo  = new ConnectionInfo(host, port, username, new PrivateKeyAuthenticationMethod(username, keyFile));

            using var sftp = new SftpClient(connectionInfo);
            sftp.Connect();

            // Idempotency: skip if final file already exists (prior run completed SFTP)
            if (sftp.Exists(finalPath))
            {
                _logger.LogInformation("[PEWO][SFTP] Final file already exists at {Path} — skipping upload", finalPath);
                sftp.Disconnect();
                return StepResult.Ok($"sftp:exists:{finalPath}");
            }

            // Upload to temp name, then rename atomically
            sftp.UploadFile(zipStream, tempPath, canOverride: true);
            sftp.RenameFile(tempPath, finalPath);
            sftp.Disconnect();

            _logger.LogInformation("[PEWO][SFTP] Delivered {Path}", finalPath);
            return StepResult.Ok($"sftp:delivered:{finalPath}");
        }

        private record SftpConfig
        {
            public string RemotePath { get; init; } = "/nexgen/delivery";
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Step 5 — ARCHIVE
    // Blob SDK. Copy originals to flexcount-save → delete source blobs.
    // Idempotent: skip blobs already archived.
    // ─────────────────────────────────────────────────────────────────────────

    public class ArchiveStep
    {
        private readonly BlobServiceClient _blobServiceClient;
        private readonly ILogger<ArchiveStep> _logger;

        public ArchiveStep(BlobServiceClient blobServiceClient, ILogger<ArchiveStep> logger)
        {
            _blobServiceClient = blobServiceClient;
            _logger = logger;
        }

        public async Task<StepResult> ExecuteAsync(PewoStepContext ctx, CancellationToken cancellationToken)
        {
            var cfg = JsonSerializer.Deserialize<ArchiveConfig>(ctx.ConfigJson ?? "{}") ?? new();

            _logger.LogInformation("[PEWO][ARCHIVE] RunId={RunId} ArchiveContainer={Container} DryRun={DryRun}",
                ctx.RunId, cfg.ArchiveContainer, ctx.DryRun);

            if (ctx.InMemoryFiles.Count == 0)
                return StepResult.Fail("No in-memory files to archive (READ_BLOB must succeed first)");

            var sourceContainer  = _blobServiceClient.GetBlobContainerClient("fc-hold-target");
            var archiveContainer = _blobServiceClient.GetBlobContainerClient(cfg.ArchiveContainer);
            var archived         = 0;

            foreach (var blobName in ctx.InMemoryFiles.Keys)
            {
                var archiveBlobName = $"{ctx.RunId}/{blobName}";
                var archiveBlob     = archiveContainer.GetBlobClient(archiveBlobName);

                // Skip if already archived (idempotency)
                if (await archiveBlob.ExistsAsync(cancellationToken))
                {
                    _logger.LogInformation("[PEWO][ARCHIVE] Already archived: {Name}", archiveBlobName);
                    archived++;
                    continue;
                }

                if (!ctx.DryRun)
                {
                    var sourceBlob = sourceContainer.GetBlobClient(blobName);
                    // Copy to archive
                    await archiveBlob.StartCopyFromUriAsync(sourceBlob.Uri, cancellationToken: cancellationToken);
                    // Poll until copy complete
                    BlobProperties props;
                    do
                    {
                        await Task.Delay(500, cancellationToken);
                        props = await archiveBlob.GetPropertiesAsync(cancellationToken: cancellationToken);
                    } while (props.CopyStatus == CopyStatus.Pending);

                    if (props.CopyStatus != CopyStatus.Success)
                        return StepResult.Fail($"Copy failed for {blobName}: {props.CopyStatusDescription}");

                    // Delete source
                    await sourceBlob.DeleteIfExistsAsync(cancellationToken: cancellationToken);
                    _logger.LogInformation("[PEWO][ARCHIVE] Archived and deleted: {Name}", blobName);
                }
                else
                {
                    _logger.LogInformation("[PEWO][ARCHIVE] DryRun — would archive: {Name}", blobName);
                }

                archived++;
            }

            return StepResult.Ok($"archived:{archived}");
        }

        private record ArchiveConfig
        {
            public string ArchiveContainer { get; init; } = "flexcount-save";
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Step 6 — EMAIL
    // SendGrid REST API. artifact_ref guard prevents duplicate send on retry.
    // ─────────────────────────────────────────────────────────────────────────

    public class EmailStep
    {
        private readonly ILogger<EmailStep> _logger;

        public EmailStep(ILogger<EmailStep> logger)
        {
            _logger = logger;
        }

        public async Task<StepResult> ExecuteAsync(PewoStepContext ctx, string? existingArtifactRef, CancellationToken cancellationToken)
        {
            // Idempotency guard — already notified on a prior attempt
            if (!string.IsNullOrEmpty(existingArtifactRef) && existingArtifactRef.StartsWith("notified:"))
            {
                _logger.LogInformation("[PEWO][EMAIL] Already notified ({ArtifactRef}) — skipping", existingArtifactRef);
                return StepResult.Ok(existingArtifactRef);
            }

            var cfg = JsonSerializer.Deserialize<EmailConfig>(ctx.ConfigJson ?? "{}") ?? new();

            _logger.LogInformation("[PEWO][EMAIL] RunId={RunId} Recipients={Count} DryRun={DryRun}",
                ctx.RunId, cfg.Recipients.Count, ctx.DryRun);

            var apiKey   = Environment.GetEnvironmentVariable("SendGridEmailKey") ?? throw new InvalidOperationException("SendGridEmailKey not set");
            var fromAddr = Environment.GetEnvironmentVariable("SendGridEmail")    ?? throw new InvalidOperationException("SendGridEmail not set");

            if (ctx.DryRun)
            {
                _logger.LogInformation("[PEWO][EMAIL] DryRun — skipping SendGrid send");
                return StepResult.Ok($"notified:dryrun:{DateTime.UtcNow:o}");
            }

            var client  = new SendGridClient(apiKey);
            var from    = new EmailAddress(fromAddr, "FlexCount PEWO");
            var subject = $"{cfg.Subject} [{ctx.RunId}]";
            var body    = BuildEmailBody(ctx);

            var toList = cfg.Recipients.Select(r => new EmailAddress(r)).ToList();
            var msg    = MailHelper.CreateSingleEmailToMultipleRecipients(from, toList, subject, body, body);

            var response = await client.SendEmailAsync(msg, cancellationToken);

            if ((int)response.StatusCode >= 400)
            {
                var respBody = await response.Body.ReadAsStringAsync(cancellationToken);
                return StepResult.Fail($"SendGrid error {response.StatusCode}: {respBody}");
            }

            var artifactRef = $"notified:{DateTime.UtcNow:o}";
            _logger.LogInformation("[PEWO][EMAIL] Sent. RunId={RunId} ArtifactRef={Ref}", ctx.RunId, artifactRef);
            return StepResult.Ok(artifactRef);
        }

        private static string BuildEmailBody(PewoStepContext ctx)
            => $@"<html><body>
<h2>FlexCount PEWO — Workflow Completed</h2>
<p><strong>Run ID:</strong> {ctx.RunId}</p>
<p><strong>Workflow:</strong> {ctx.WorkflowCode}</p>
<p><strong>Completed At:</strong> {DateTime.UtcNow:u}</p>
<p>TARGET NexGen GM + PRPC files have been validated, zipped, delivered via SFTP, and archived.</p>
</body></html>";

        private record EmailConfig
        {
            public List<string> Recipients { get; init; } = new();
            public string Subject { get; init; } = "[FlexCount] TARGET NexGen files delivered";
        }
    }
}
