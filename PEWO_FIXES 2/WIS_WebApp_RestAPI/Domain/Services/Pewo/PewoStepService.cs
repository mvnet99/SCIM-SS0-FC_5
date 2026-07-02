using Azure.Storage.Blobs;
using Domain.ApiModels.Pewo;
using Domain.Constants;
using Domain.Helpers.Interfaces;
using Domain.Services.Interfaces;
using Domain.Services.Interfaces.Pewo;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SendGrid;
using SendGrid.Helpers.Mail;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WS.FC.Dapper.Shared.DTOs.Pewo;

namespace Domain.Services.Pewo;

/// <summary>
/// All PEWO step implementations.
///
/// Fixes applied:
///   Fix 2  — SFTP partial delivery tracking. Each delivered zip persisted in
///             sftp:partial: artifact_ref after delivery. Retry skips already-delivered
///             zips — no duplicate SFTP delivery.
///   Fix 3  — Archive cross-check. If source empty on retry, checks flexcount-save/{runId}/
///             to distinguish already-archived (success) from genuinely missing files (fail).
///   Fix 4  — EMAIL_SUMMARY sends on last attempt regardless of batch state.
///             Includes warning in email body if runs still active or any failed.
///   Fix 5  — Blob connection string resolved at point of use via GetBlobConnectionString().
///             Prevents null cache if Key Vault unavailable at API startup.
///   Fix 6  — Temp file guaranteed cleanup in finally block + startup stale file purge.
///   Fix 7  — SendGrid 429 rate limit handled explicitly — returns Fail so retry/backoff
///             handles the wait rather than throwing an unhandled exception.
/// </summary>
public class PewoStepService : IPewoStepService
{
    private readonly ITotalsValidationService  _totalsValidationService;
    private readonly IAzureBlobStorageHelper   _blobHelper;
    private readonly IFtpHelper                _ftpHelper;
    private readonly IPewoJobDataService       _pewoJobDataService;
    private readonly IConfiguration            _configuration;
    private readonly ILogger<PewoStepService>  _logger;

    public PewoStepService(
        ITotalsValidationService  totalsValidationService,
        IAzureBlobStorageHelper   blobHelper,
        IFtpHelper                ftpHelper,
        IPewoJobDataService       pewoJobDataService,
        IConfiguration            configuration,
        ILogger<PewoStepService>  logger)
    {
        _totalsValidationService = totalsValidationService;
        _blobHelper              = blobHelper;
        _ftpHelper               = ftpHelper;
        _pewoJobDataService      = pewoJobDataService;
        _configuration           = configuration;
        _logger                  = logger;
        // Fix 5: No longer caching blob connection string in constructor.
        // Resolved at point of use via GetBlobConnectionString() to prevent null cache
        // if Key Vault is temporarily unavailable at API startup.
    }

    // ── Fix 5: Blob connection string resolved at point of use ────────────────
    private string GetBlobConnectionString()
    {
        var connStr = _configuration[WISAppConstants.VaultBlobKey];
        if (string.IsNullOrWhiteSpace(connStr))
            throw new InvalidOperationException(
                "[PEWO] Blob connection string not available from Key Vault (VaultBlobKey). " +
                "Check Key Vault configuration and managed identity permissions.");
        return connStr;
    }

    // ── TOTALS_CHECK ──────────────────────────────────────────────────────────

    public PewoStepResponse TotalsCheck(PewoStepRequest request)
    {
        _logger.LogInformation("[PEWO] TOTALS_CHECK RunId={RunId} Store={Store}",
            request.Id_WorkflowRun, request.Store_No);
        try
        {
            if (string.IsNullOrWhiteSpace(request.Event_Guid))
                return Fail("TOTALS_CHECK: Event_Guid missing. Confirm event-close call included Event_Guid.");

            var result = _totalsValidationService.ValidateNgen(request.Event_Guid);

            if (!result.Success)
            {
                var errors = string.Join("; ", result.Messages
                    .Where(m => m.Status == "Error").Select(m => m.Step));
                _logger.LogWarning("[PEWO] TOTALS_CHECK RunId={RunId} FAILED: {Errors}",
                    request.Id_WorkflowRun, errors);
                return Fail($"TotalsCheck validation failed: {errors}");
            }

            var artifactRef = JsonSerializer.Serialize(new
            {
                totalQty  = result.TotalQty,
                totalExt  = result.TotalExt,
                eventGuid = request.Event_Guid
            });

            _logger.LogInformation("[PEWO] TOTALS_CHECK RunId={RunId} PASSED Qty={Qty} Ext={Ext}",
                request.Id_WorkflowRun, result.TotalQty, result.TotalExt);
            return Success(artifactRef);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[PEWO] TOTALS_CHECK RunId={RunId} exception", request.Id_WorkflowRun);
            return Fail($"Exception: {ex.Message}");
        }
    }

    // ── GET_EVENTS (MEO stub) ─────────────────────────────────────────────────

    public async Task<PewoStepResponse> GetEventsAsync(PewoStepRequest request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("[PEWO] GET_EVENTS RunId={RunId}", request.Id_WorkflowRun);
        try
        {
            if (!string.IsNullOrEmpty(request.Artifact_Ref) &&
                request.Artifact_Ref.StartsWith("discovered:", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogInformation("[PEWO] GET_EVENTS RunId={RunId} already discovered — skipping",
                    request.Id_WorkflowRun);
                return Success(request.Artifact_Ref);
            }

            var config          = ParseConfig(request.Config);
            var sourceContainer = config.TryGetValue("sourceContainer", out var sc) ? sc : "fc-hold-target";
            var filePattern     = config.TryGetValue("filePattern",     out var fp) ? fp : string.Empty;

            var container  = new BlobContainerClient(GetBlobConnectionString(), sourceContainer);
            var discovered = new List<string>();

            await foreach (var blobItem in container.GetBlobsAsync(prefix: filePattern,
                cancellationToken: cancellationToken))
            {
                discovered.Add(blobItem.Name);
            }

            _logger.LogInformation("[PEWO] GET_EVENTS RunId={RunId} discovered {Count} blobs",
                request.Id_WorkflowRun, discovered.Count);

            return Success($"discovered:{discovered.Count}:{DateTime.UtcNow:yyyyMMdd}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[PEWO] GET_EVENTS RunId={RunId} exception", request.Id_WorkflowRun);
            return Fail($"Exception: {ex.Message}");
        }
    }

    // ── TRANSFORM (MEO stub) ──────────────────────────────────────────────────

    public async Task<PewoStepResponse> TransformAsync(PewoStepRequest request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("[PEWO] TRANSFORM RunId={RunId}", request.Id_WorkflowRun);
        // TODO (MEO): EBCDIC conversion for icnts.dat, ASCII for cpinv.dat and CTL
        // EBCDICFileHelper.cs already exists for EBCDIC conversion.
        // Blocked on MEO file location in blob storage being confirmed.
        _logger.LogWarning("[PEWO] TRANSFORM RunId={RunId} — stub. MEO conversion pending.",
            request.Id_WorkflowRun);
        return await Task.FromResult(Success($"transform-stub:{request.Id_WorkflowRun}:{DateTime.UtcNow:O}"));
    }

    // ── READ_BLOB_ZIP ─────────────────────────────────────────────────────────

    public async Task<PewoStepResponse> ReadBlobZipAsync(PewoStepRequest request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("[PEWO] READ_BLOB_ZIP RunId={RunId} EventGuid={Guid}",
            request.Id_WorkflowRun, request.Event_Guid);
        try
        {
            if (!string.IsNullOrEmpty(request.Artifact_Ref) &&
                request.Artifact_Ref.StartsWith("staged:", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogInformation("[PEWO] READ_BLOB_ZIP RunId={RunId} already staged — skipping",
                    request.Id_WorkflowRun);
                return Success(request.Artifact_Ref);
            }

            if (string.IsNullOrWhiteSpace(request.Event_Guid))
                return Fail("READ_BLOB_ZIP: Event_Guid missing — cannot locate source files.");

            var config           = ParseConfig(request.Config);
            var sourceContainer  = config.TryGetValue("sourceContainer",  out var sc)  ? sc  : WISAppConstants.OutputFilesContainer;
            var stagingContainer = config.TryGetValue("stagingContainer",  out var stg) ? stg : "flexcount-save";

            var blobContainer = new BlobContainerClient(GetBlobConnectionString(), sourceContainer);
            var prefix        = request.Event_Guid + "/";
            var sourceFiles   = new List<string>();

            await foreach (var blobItem in blobContainer.GetBlobsAsync(prefix: prefix,
                cancellationToken: cancellationToken))
            {
                var name = blobItem.Name;
                if (name.Contains("tar_itm_gm",   StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("tar_itm_prpc",  StringComparison.OrdinalIgnoreCase))
                    sourceFiles.Add(name);
            }

            if (!sourceFiles.Any())
                return Fail($"READ_BLOB_ZIP: No GM or PRPC files found in '{sourceContainer}/{prefix}'. " +
                            "Confirm backend process generated files for this event.");

            var zipsCreated = new List<string>();

            foreach (var blobPath in sourceFiles)
            {
                var sourceFileName = Path.GetFileName(blobPath);
                var zipFileName    = Path.ChangeExtension(sourceFileName, ".zip");

                var fileStream = await _blobHelper.DownloadFileBlob(sourceContainer, blobPath);
                if (fileStream == null || fileStream.Length == 0)
                    return Fail($"READ_BLOB_ZIP: Source blob '{blobPath}' downloaded empty.");

                using var zipStream = new MemoryStream();
                using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create, leaveOpen: true))
                {
                    var entry = archive.CreateEntry(sourceFileName, CompressionLevel.Optimal);
                    await using var entryStream = entry.Open();
                    fileStream.Position = 0;
                    await fileStream.CopyToAsync(entryStream, cancellationToken);
                }

                zipStream.Position = 0;
                await _blobHelper.UploadFileAsync(stagingContainer, zipStream, zipFileName);
                zipsCreated.Add(zipFileName);

                _logger.LogInformation("[PEWO] READ_BLOB_ZIP RunId={RunId} created {Zip} in {Container}",
                    request.Id_WorkflowRun, zipFileName, stagingContainer);
            }

            return Success($"staged:{string.Join(",", zipsCreated)}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[PEWO] READ_BLOB_ZIP RunId={RunId} exception", request.Id_WorkflowRun);
            return Fail($"Exception: {ex.Message}");
        }
    }

    // ── SFTP ──────────────────────────────────────────────────────────────────
    // Fix 2: Partial delivery tracking via sftp:partial: artifact_ref prefix.
    //        After each successful zip delivery, persists progress to DB so retry
    //        knows which zips are already delivered and skips them.
    //        Prevents duplicate SFTP delivery on partial failure + retry.
    // Fix 6: Temp file guaranteed cleanup in finally block.
    //        Startup stale file purge via CleanupStalePewoTempFiles().

    public async Task<PewoStepResponse> SftpAsync(PewoStepRequest request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("[PEWO] SFTP RunId={RunId}", request.Id_WorkflowRun);

        // Fix 6: Clean up any stale temp files from previously crashed executions
        CleanupStalePewoTempFiles();

        try
        {
            if (!string.IsNullOrEmpty(request.Artifact_Ref) &&
                request.Artifact_Ref.StartsWith("sftp:delivered:", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogInformation("[PEWO] SFTP RunId={RunId} already delivered — skipping",
                    request.Id_WorkflowRun);
                return Success(request.Artifact_Ref);
            }

            var config           = ParseConfig(request.Config);
            var remotePath       = config.TryGetValue("remotePath",       out var rp)  ? rp  : string.Empty;
            var stagingContainer = config.TryGetValue("stagingContainer",  out var stg) ? stg : "flexcount-save";

            if (string.IsNullOrWhiteSpace(remotePath))
                return Fail("SFTP: 'remotePath' missing from step Config JSON.");

            // Parse staged: artifact_ref from READ_BLOB_ZIP
            var priorRef = request.Artifact_Ref ?? string.Empty;
            if (!priorRef.StartsWith("staged:",  StringComparison.OrdinalIgnoreCase) &&
                !priorRef.StartsWith("sftp:partial:", StringComparison.OrdinalIgnoreCase))
                return Fail("SFTP: Expected artifact_ref in 'staged:' or 'sftp:partial:' format. " +
                            "Ensure READ_BLOB_ZIP ran and succeeded before SFTP.");

            // Parse full zip list from staged: and already-delivered from sftp:partial:
            var allZipNames = priorRef.StartsWith("staged:", StringComparison.OrdinalIgnoreCase)
                ? priorRef["staged:".Length..].Split(',', StringSplitOptions.RemoveEmptyEntries).Select(z => z.Trim()).ToList()
                : priorRef["sftp:partial:".Length..].Split(',', StringSplitOptions.RemoveEmptyEntries).Select(z => z.Trim()).ToList();

            // Fix 2: For partial retries, re-read full zip list from the previous step's artifact_ref
            // The staged: list is the authoritative source — partial: just tracks what was delivered
            List<string> zipNamesToDeliver;
            HashSet<string> alreadyDelivered;

            if (priorRef.StartsWith("sftp:partial:", StringComparison.OrdinalIgnoreCase))
            {
                // On retry: check prior step's artifact_ref to get full list
                var priorStepRef = await GetPriorStepArtifactRefAsync(
                    request.Id_WorkflowRun, "READ_BLOB_ZIP", cancellationToken);

                var fullList = priorStepRef?.StartsWith("staged:", StringComparison.OrdinalIgnoreCase) == true
                    ? priorStepRef["staged:".Length..].Split(',', StringSplitOptions.RemoveEmptyEntries)
                        .Select(z => z.Trim()).ToList()
                    : new List<string>();

                alreadyDelivered  = new HashSet<string>(allZipNames, StringComparer.OrdinalIgnoreCase);
                zipNamesToDeliver = fullList.Where(z => !alreadyDelivered.Contains(z)).ToList();

                _logger.LogInformation("[PEWO] SFTP RunId={RunId} partial retry — {Already} already delivered, {Remaining} remaining",
                    request.Id_WorkflowRun, alreadyDelivered.Count, zipNamesToDeliver.Count);
            }
            else
            {
                zipNamesToDeliver = allZipNames;
                alreadyDelivered  = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }

            if (!zipNamesToDeliver.Any() && !alreadyDelivered.Any())
                return Fail("SFTP: No zip names found in artifact_ref.");

            var deliveredFiles = new List<string>(alreadyDelivered);

            foreach (var zipName in zipNamesToDeliver)
            {
                string? tempPath = null;
                try
                {
                    var zipStream = await _blobHelper.DownloadFileBlob(stagingContainer, zipName);
                    if (zipStream == null || zipStream.Length == 0)
                        return Fail($"SFTP: Staging zip '{zipName}' not found or empty in {stagingContainer}.");

                    // Fix 6: Unique temp file name for cleanup identification
                    tempPath = Path.Combine(Path.GetTempPath(),
                        $"pewo_{request.Id_WorkflowRun}_{zipName}_{DateTime.UtcNow:yyyyMMddHHmmss}");

                    zipStream.Position = 0;
                    await using (var fs = new FileStream(tempPath, FileMode.Create, FileAccess.Write))
                        await zipStream.CopyToAsync(fs, cancellationToken);

                    string uploadResult;
                    await using (var fileStream = new FileStream(tempPath, FileMode.Open, FileAccess.Read))
                        uploadResult = await _ftpHelper.UploadFile(fileStream, zipName, remotePath, _logger);

                    if (uploadResult != MessageConstants.Uploaded)
                        return Fail($"SFTP upload failed for '{zipName}': {uploadResult}");

                    deliveredFiles.Add(zipName);

                    // Fix 2: Persist partial progress after each successful delivery
                    // If next delivery fails, retry knows what was already delivered
                    await _pewoJobDataService.UpsertStepRunAsync(
                        request.Id_WorkflowRun, request.Id_WorkflowStepDef, request.Step_Kind,
                        "PENDING", request.Attempts,
                        $"sftp:partial:{string.Join(",", deliveredFiles)}",
                        null, null, null, cancellationToken);

                    _logger.LogInformation("[PEWO] SFTP RunId={RunId} delivered {File} to {Path}",
                        request.Id_WorkflowRun, zipName, remotePath);
                }
                finally
                {
                    // Fix 6: Guaranteed temp file cleanup regardless of exception
                    try
                    {
                        if (tempPath != null && File.Exists(tempPath))
                            File.Delete(tempPath);
                    }
                    catch (Exception cleanupEx)
                    {
                        _logger.LogWarning(cleanupEx, "[PEWO] SFTP failed to delete temp file {Path}", tempPath);
                    }
                }
            }

            return Success($"sftp:delivered:{remotePath}:{string.Join(",", deliveredFiles)}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[PEWO] SFTP RunId={RunId} exception", request.Id_WorkflowRun);
            return Fail($"Exception: {ex.Message}");
        }
    }

    // ── ARCHIVE ───────────────────────────────────────────────────────────────
    // Fix 3: Cross-check flexcount-save/{runId}/ when source files missing on retry.
    //        Distinguishes already-archived (idempotent success) from genuinely
    //        missing files (genuine failure requiring investigation).

    public async Task<PewoStepResponse> ArchiveAsync(PewoStepRequest request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("[PEWO] ARCHIVE RunId={RunId} EventGuid={Guid}",
            request.Id_WorkflowRun, request.Event_Guid);
        try
        {
            if (!string.IsNullOrEmpty(request.Artifact_Ref) &&
                request.Artifact_Ref.StartsWith("archived:", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogInformation("[PEWO] ARCHIVE RunId={RunId} already archived — skipping",
                    request.Id_WorkflowRun);
                return Success(request.Artifact_Ref);
            }

            if (string.IsNullOrWhiteSpace(request.Event_Guid))
                return Fail("ARCHIVE: Event_Guid missing — cannot locate source files.");

            var config           = ParseConfig(request.Config);
            var sourceContainer  = config.TryGetValue("sourceContainer",  out var sc) ? sc : WISAppConstants.OutputFilesContainer;
            var archiveContainer = config.TryGetValue("archiveContainer",  out var ac) ? ac : "flexcount-save";

            var blobConnStr   = GetBlobConnectionString();
            var blobContainer = new BlobContainerClient(blobConnStr, sourceContainer);
            var prefix        = request.Event_Guid + "/";
            var sourceFiles   = new List<string>();

            await foreach (var blobItem in blobContainer.GetBlobsAsync(prefix: prefix,
                cancellationToken: cancellationToken))
            {
                var name = blobItem.Name;
                if (name.Contains("tar_itm_gm",   StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("tar_itm_prpc",  StringComparison.OrdinalIgnoreCase))
                    sourceFiles.Add(name);
            }

            if (!sourceFiles.Any())
            {
                // Fix 3: Source files missing — cross-check archive to determine if already done
                var archiveBlobContainer = new BlobContainerClient(blobConnStr, archiveContainer);
                var archivePrefix        = $"{request.Id_WorkflowRun}/";
                var archiveHasFiles      = false;

                await foreach (var archiveBlob in archiveBlobContainer.GetBlobsAsync(
                    prefix: archivePrefix, cancellationToken: cancellationToken))
                {
                    archiveHasFiles = true;
                    break;
                }

                if (archiveHasFiles)
                {
                    _logger.LogInformation(
                        "[PEWO] ARCHIVE RunId={RunId} source empty but archive exists — already archived on prior attempt",
                        request.Id_WorkflowRun);
                    return Success($"archived:already:{request.Event_Guid}");
                }
                else
                {
                    // Source gone AND archive empty — genuine failure, needs investigation
                    _logger.LogError(
                        "[PEWO] ARCHIVE RunId={RunId} source empty in '{Container}/{Prefix}' " +
                        "AND archive empty in '{ArchiveContainer}/{ArchivePrefix}'. " +
                        "Files may have been deleted from source before archiving.",
                        request.Id_WorkflowRun, sourceContainer, prefix, archiveContainer, archivePrefix);
                    return Fail(
                        $"ARCHIVE: No source files in '{sourceContainer}/{prefix}' and no archive in " +
                        $"'{archiveContainer}/{archivePrefix}'. Manual investigation required.");
                }
            }

            var archivedCount = 0;

            foreach (var blobPath in sourceFiles)
            {
                var sourceFileName  = Path.GetFileName(blobPath);
                var archiveBlobName = $"{request.Id_WorkflowRun}/{sourceFileName}";

                var fileStream = await _blobHelper.DownloadFileBlob(sourceContainer, blobPath);
                if (fileStream == null || fileStream.Length == 0)
                {
                    _logger.LogWarning("[PEWO] ARCHIVE RunId={RunId} blob {Path} empty — skipping",
                        request.Id_WorkflowRun, blobPath);
                    continue;
                }

                fileStream.Position = 0;
                await _blobHelper.UploadFileAsync(archiveContainer, fileStream, archiveBlobName);
                archivedCount++;

                _logger.LogInformation("[PEWO] ARCHIVE RunId={RunId} archived {Source} → {Dest}",
                    request.Id_WorkflowRun, blobPath, $"{archiveContainer}/{archiveBlobName}");
            }

            // Zips in flexcount-save already archived from READ_BLOB_ZIP — no action needed.
            // Source originals NOT deleted — eventGuid subfolder is unique per event.
            return Success($"archived:{archivedCount}:{request.Event_Guid}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[PEWO] ARCHIVE RunId={RunId} exception", request.Id_WorkflowRun);
            return Fail($"Exception: {ex.Message}");
        }
    }

    // ── EMAIL ─────────────────────────────────────────────────────────────────

    public async Task<PewoStepResponse> EmailAsync(PewoStepRequest request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("[PEWO] EMAIL RunId={RunId}", request.Id_WorkflowRun);
        try
        {
            if (!string.IsNullOrEmpty(request.Artifact_Ref) &&
                request.Artifact_Ref.StartsWith("notified:", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogInformation("[PEWO] EMAIL RunId={RunId} already sent — skipping",
                    request.Id_WorkflowRun);
                return Success(request.Artifact_Ref);
            }

            var config     = ParseConfig(request.Config);
            var subject    = config.TryGetValue("subject",    out var s) ? s : "PEWO — Workflow Completed";
            var recipients = GetRecipients(config);

            if (!recipients.Any())
                return Fail("EMAIL: No recipients configured in step Config JSON.");

            var body = BuildNotificationEmailBody(request.Id_WorkflowRun, request.Store_No, request.Event_Guid);
            await SendEmailAsync(recipients, subject, body, cancellationToken);

            _logger.LogInformation("[PEWO] EMAIL RunId={RunId} sent to {Count} recipients",
                request.Id_WorkflowRun, recipients.Count);
            return Success($"notified:{DateTime.UtcNow:O}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[PEWO] EMAIL RunId={RunId} exception", request.Id_WorkflowRun);
            return Fail($"Exception: {ex.Message}");
        }
    }

    // ── EMAIL_SUMMARY ─────────────────────────────────────────────────────────
    // Fix 4: On last attempt, sends summary regardless of batch state.
    //        Includes warning in email body if runs still active or any failed.

    public async Task<PewoStepResponse> EmailSummaryAsync(PewoStepRequest request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("[PEWO] EMAIL_SUMMARY RunId={RunId}", request.Id_WorkflowRun);
        try
        {
            if (!string.IsNullOrEmpty(request.Artifact_Ref) &&
                request.Artifact_Ref.StartsWith("summary-sent:", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogInformation("[PEWO] EMAIL_SUMMARY RunId={RunId} already sent — skipping",
                    request.Id_WorkflowRun);
                return Success(request.Artifact_Ref);
            }

            var config                = ParseConfig(request.Config);
            var batchWorkflowTypeCode = config.TryGetValue("batchWorkflowTypeCode", out var bwt)
                ? bwt : "GM_PRC_DELIVERY";
            var subject    = config.TryGetValue("subject",    out var s) ? s : "TARGET GM + PRPC Delivery Summary";
            var recipients = GetRecipients(config);

            if (!recipients.Any())
                return Fail("EMAIL_SUMMARY: No recipients configured in step Config JSON.");

            if (string.IsNullOrWhiteSpace(batchWorkflowTypeCode))
                return Fail("EMAIL_SUMMARY: 'batchWorkflowTypeCode' missing from step Config JSON.");

            var todayKey          = DateTime.UtcNow.ToString("yyyy-MM-dd");
            var batchRuns         = await _pewoJobDataService.GetBatchRunStatusAsync(
                batchWorkflowTypeCode, todayKey, cancellationToken);
            var stillActive       = batchRuns.Count(r => r.Status == "PENDING" || r.Status == "RUNNING");
            var maxAttemptsReached = request.Attempts >= request.Max_Attempts;

            // Fix 4: Wait gate — return Fail while batch active unless on last attempt
            if (stillActive > 0 && !maxAttemptsReached)
            {
                _logger.LogInformation(
                    "[PEWO] EMAIL_SUMMARY RunId={RunId} batch not settled — {Active}/{Total} still active (attempt {A}/{Max})",
                    request.Id_WorkflowRun, stillActive, batchRuns.Count, request.Attempts, request.Max_Attempts);
                return Fail($"Batch not fully terminal — {stillActive} of {batchRuns.Count} still in progress. Will retry.");
            }

            // Fix 4: Send on last attempt regardless — warn if batch incomplete or has failures
            bool hasStillActive = stillActive > 0 && maxAttemptsReached;
            bool hasFailures    = batchRuns.Any(r => r.Status == "FAILED");

            var body = BuildSummaryEmailBody(todayKey, batchRuns, hasStillActive, hasFailures);
            await SendEmailAsync(recipients, subject, body, cancellationToken);

            _logger.LogInformation("[PEWO] EMAIL_SUMMARY RunId={RunId} sent for batch {Key} ({Count} events)",
                request.Id_WorkflowRun, todayKey, batchRuns.Count);
            return Success($"summary-sent:{todayKey}:{batchRuns.Count}events");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[PEWO] EMAIL_SUMMARY RunId={RunId} exception", request.Id_WorkflowRun);
            return Fail($"Exception: {ex.Message}");
        }
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    // Fix 7: SendGrid 429 rate limit handled explicitly
    private async Task SendEmailAsync(List<string> recipients, string subject, string htmlBody,
        CancellationToken cancellationToken)
    {
        var apiKey      = _configuration[_configuration[WISAppConstants.SendGridEmailKey]];
        var fromAddress = _configuration[_configuration[WISAppConstants.SendGridEmail]];

        if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(fromAddress))
            throw new InvalidOperationException(
                "SendGrid configuration missing. Check KeyVaultSettings:SendGridEmailKey and KeyVaultSettings:SendGridEmail.");

        var client  = new SendGridClient(apiKey);
        var from    = new EmailAddress(fromAddress, "FlexCount PEWO");
        var toList  = recipients.Select(r => new EmailAddress(r.Trim())).ToList();
        var message = MailHelper.CreateSingleEmailToMultipleRecipients(from, toList, subject, htmlBody, htmlBody);

        var response = await client.SendEmailAsync(message, cancellationToken);

        // Fix 7: Explicit 429 handling — throw descriptive message so EmailAsync returns Fail
        // Retry/backoff handles the wait automatically via the step failure mechanism
        if ((int)response.StatusCode == 429)
            throw new InvalidOperationException(
                "SendGrid rate limited (429) — retry/backoff will handle the wait. " +
                "If this recurs consider increasing Backoff_Seconds on the EMAIL step definition.");

        if ((int)response.StatusCode >= 400)
        {
            var body = await response.Body.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException($"SendGrid returned {(int)response.StatusCode}: {body}");
        }
    }

    private async Task<string?> GetPriorStepArtifactRefAsync(
        int runId, string stepKind, CancellationToken cancellationToken)
    {
        try
        {
            var steps = await _pewoJobDataService.GetRunResumeAsync(runId, 0, cancellationToken);
            return steps.FirstOrDefault(s =>
                string.Equals(s.Step_Kind, stepKind, StringComparison.OrdinalIgnoreCase))?.Artifact_Ref;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[PEWO] GetPriorStepArtifactRefAsync RunId={RunId} Step={Kind} failed",
                runId, stepKind);
            return null;
        }
    }

    // Fix 6: Cleanup stale temp files from previously crashed executions
    private void CleanupStalePewoTempFiles()
    {
        try
        {
            var tempPath = Path.GetTempPath();
            foreach (var file in Directory.GetFiles(tempPath, "pewo_*"))
            {
                var fileInfo = new FileInfo(file);
                if (fileInfo.CreationTimeUtc < DateTime.UtcNow.AddHours(-1))
                {
                    File.Delete(file);
                    _logger.LogInformation("[PEWO] Cleaned up stale temp file {File}", file);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[PEWO] Stale temp file cleanup failed — non-critical");
        }
    }

    private static string BuildNotificationEmailBody(int runId, string? storeNo, string? eventGuid) =>
        $@"<html><body>
<h2>FlexCount PEWO — Workflow Completed</h2>
<table border='1' cellpadding='6'>
  <tr><td><strong>Run ID</strong></td><td>{runId}</td></tr>
  <tr><td><strong>Store</strong></td><td>{storeNo ?? "—"}</td></tr>
  <tr><td><strong>Event GUID</strong></td><td>{eventGuid ?? "—"}</td></tr>
  <tr><td><strong>Completed At (UTC)</strong></td><td>{DateTime.UtcNow:u}</td></tr>
</table>
<p>Post-event NGen files have been validated, zipped, delivered via SFTP, and archived.</p>
</body></html>";

    // Fix 4: Updated to include warnings for still-active and failed runs
    private static string BuildSummaryEmailBody(
        string todayKey, List<BatchRunStatusDto> batch, bool hasStillActive, bool hasFailures)
    {
        var sb = new StringBuilder();
        sb.Append("<html><body>");
        sb.Append($"<h2>TARGET GM + PRPC Delivery Summary — {todayKey}</h2>");

        if (hasStillActive)
            sb.Append("<p><strong>⚠️ WARNING:</strong> Some events were still in progress when this summary was sent (retry limit reached). Manual investigation may be required.</p>");

        if (hasFailures)
            sb.Append("<p><strong>⚠️ WARNING:</strong> One or more delivery runs failed permanently. Check FAILED rows below and Pewo_WorkflowRunLog for details.</p>");

        if (!batch.Any())
        {
            sb.Append("<p>No GM/PRPC deliveries were due today.</p>");
        }
        else
        {
            sb.Append("<table border='1' cellpadding='6'>");
            sb.Append("<tr><th>Run ID</th><th>Store</th><th>Event Date</th><th>Status</th><th>Details</th></tr>");
            foreach (var row in batch.OrderBy(b => b.Store_No))
            {
                var rowColor = row.Status == "FAILED" ? " bgcolor='#FFE0E0'" : row.Status == "COMPLETED" ? " bgcolor='#E0FFE0'" : "";
                sb.Append($"<tr{rowColor}><td>{row.Id_WorkflowRun}</td><td>{row.Store_No}</td>" +
                          $"<td>{row.Event_Date:yyyy-MM-dd}</td><td>{row.Status}</td><td>{row.Reason ?? "—"}</td></tr>");
            }
            sb.Append("</table>");
        }

        sb.Append("</body></html>");
        return sb.ToString();
    }

    private static List<string> GetRecipients(Dictionary<string, string> config)
    {
        if (!config.TryGetValue("recipients", out var r) || string.IsNullOrWhiteSpace(r))
            return new List<string>();
        return r.Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
    }

    private static Dictionary<string, string> ParseConfig(string? configJson)
    {
        if (string.IsNullOrWhiteSpace(configJson))
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var doc = JsonDocument.Parse(configJson);
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var prop in doc.RootElement.EnumerateObject())
                dict[prop.Name] = prop.Value.ValueKind == JsonValueKind.String
                    ? prop.Value.GetString() ?? string.Empty
                    : prop.Value.ToString();
            return dict;
        }
        catch { return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); }
    }

    private static PewoStepResponse Success(string? artifactRef) =>
        new() { Success = true, Artifact_Ref = artifactRef };

    private static PewoStepResponse Fail(string reason) =>
        new() { Success = false, Failure_Details = reason };
}
