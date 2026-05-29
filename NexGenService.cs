using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Domain.ApiModels;
using Domain.Services.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Domain.Services
{
    /// <summary>
    /// Replaces post-nexgen-gm-file and post-nexgen-prpc-file Unix scripts.
    ///
    /// Original Unix logic per script:
    ///   1. Validate POSTDIR and SAVEDIR exist
    ///   2. Accept storeNumber or "ALL" — normalise to uppercase
    ///   3. Validate storeNumber is exactly 4 characters
    ///   4. Search for files matching TAR_ITM_{GM|PRPC}_{####}_{YYYYMMDD}_{HHMMSS}.txt
    ///   5. Fail if no files found (exit 2)
    ///   6. For each matched file:
    ///      a. Rename original to working copy (ORIGFILE.$LOGTIME)
    ///      b. Copy working copy back to original name (as the file to zip)
    ///      c. Zip using 7z → TAR_ITM_{GM|PRPC}_{####}_{YYYYMMDD}_{HHMMSS}.zip
    ///      d. Copy zip to SAVEDIR as backup (zip.$LOGTIME)
    ///      e. Move zip to POSTDIR (delivery folder)
    ///      f. Move working copy to SAVEDIR (original backup)
    ///      g. Delete the temp copy used for zipping
    ///   7. Log all steps and file names
    ///
    /// This service translates all of the above into in-memory streaming on Azure Blob.
    /// No files touch disk. POSTDIR = destinationContainer. SAVEDIR = archiveContainer.
    /// </summary>
    public class NexGenService : INexGenService
    {
        // File name prefixes matching the Unix scripts exactly
        private const string GmPrefix = "TAR_ITM_GM";
        private const string PrpcPrefix = "TAR_ITM_PRPC";

        // Valid FileType values
        private const string FileTypeGm = "GM";
        private const string FileTypePrpc = "PRPC";
        private const string FileTypeBoth = "BOTH";

        // Regex: TAR_ITM_{GM|PRPC}_{4-digit store}_{8-digit date}_{6-digit time}.txt
        // Mirrors: $FNBEGIN_$STRNUM_????????_??????.txt from the Unix scripts
        private static readonly Regex FileNamePattern =
            new Regex(@"^(TAR_ITM_GM|TAR_ITM_PRPC)_(\d{4})_(\d{8})_(\d{6})\.txt$",
                RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private readonly IConfiguration _configuration;
        private readonly ILogger<NexGenService> _logger;

        public NexGenService(IConfiguration configuration, ILogger<NexGenService> logger)
        {
            _configuration = configuration;
            _logger = logger;
        }

        public async Task<NexGenPostFilesResponse> PostNexGenFilesAsync(
            NexGenPostFilesRequest request,
            CancellationToken cancellationToken)
        {
            // Generate correlation ID if not provided — mirrors LOGTIME in Unix scripts
            var correlationId = string.IsNullOrWhiteSpace(request.CorrelationId)
                ? Guid.NewGuid().ToString()
                : request.CorrelationId;

            var logTime = DateTime.UtcNow.ToString("yyyyMMddHHmmss");

            var response = new NexGenPostFilesResponse
            {
                CorrelationId = correlationId,
                ClientCode = request.ClientCode,
                DryRun = request.DryRun
            };

            _logger.LogInformation(
                "[NexGen] [{CorrelationId}] Beginning NexGen file posting. Client: {ClientCode}, FileType: {FileType}, StoreNumber: {StoreNumber}, DryRun: {DryRun}",
                correlationId, request.ClientCode, request.FileType, request.StoreNumber, request.DryRun);

            try
            {
                // --- VALIDATION: mirrors Unix script argument and folder existence checks ---

                var validationErrors = ValidateRequest(request);
                if (validationErrors.Any())
                {
                    response.Success = false;
                    response.Errors.AddRange(validationErrors);
                    _logger.LogWarning("[NexGen] [{CorrelationId}] Validation failed: {Errors}",
                        correlationId, string.Join("; ", validationErrors));
                    return response;
                }

                // Normalise storeNumber to uppercase — mirrors: STRNUM=`echo $1 | tr ['a-z'] ['A-Z']`
                var storeNumber = request.StoreNumber.Trim().ToUpperInvariant();

                // Determine which file type prefixes to process
                var prefixesToProcess = ResolvePrefixes(request.FileType.Trim().ToUpperInvariant());

                var blobConnection = _configuration["BlobConnectionString"];

                // Validate source container exists — mirrors: if [ ! -d $POSTDIR ]
                if (!await ContainerExistsAsync(blobConnection, request.SourceContainer))
                {
                    var msg = $"Source container '{request.SourceContainer}' does not exist. Aborted.";
                    _logger.LogError("[NexGen] [{CorrelationId}] {Message}", correlationId, msg);
                    response.Success = false;
                    response.Errors.Add(msg);
                    return response;
                }

                // Validate destination container exists — mirrors: if [ ! -d $POSTDIR ]
                if (!await ContainerExistsAsync(blobConnection, request.DestinationContainer))
                {
                    var msg = $"Destination container '{request.DestinationContainer}' does not exist. Aborted.";
                    _logger.LogError("[NexGen] [{CorrelationId}] {Message}", correlationId, msg);
                    response.Success = false;
                    response.Errors.Add(msg);
                    return response;
                }

                // Validate archive container exists — mirrors: if [ ! -d $SAVEDIR ]
                if (!await ContainerExistsAsync(blobConnection, request.ArchiveContainer))
                {
                    var msg = $"Archive container '{request.ArchiveContainer}' does not exist. Aborted.";
                    _logger.LogError("[NexGen] [{CorrelationId}] {Message}", correlationId, msg);
                    response.Success = false;
                    response.Errors.Add(msg);
                    return response;
                }

                // Process each file type prefix
                foreach (var prefix in prefixesToProcess)
                {
                    await ProcessFilePrefixAsync(
                        blobConnection,
                        prefix,
                        storeNumber,
                        request,
                        logTime,
                        correlationId,
                        response,
                        cancellationToken);
                }

                // Mirror Unix exit 2: if no files found at all, that is an error
                if (response.FilesProcessed.Count == 0 && !response.Errors.Any())
                {
                    var pattern = BuildSearchPattern(prefixesToProcess, request.StoreNumber);
                    var msg = $"No files found matching pattern '{pattern}' in container '{request.SourceContainer}'. No files posted.";
                    _logger.LogWarning("[NexGen] [{CorrelationId}] {Message}", correlationId, msg);
                    response.Errors.Add(msg);
                    response.Success = false;
                    return response;
                }

                response.FilesCount = response.FilesProcessed.Count;
                response.Success = !response.Errors.Any();

                _logger.LogInformation(
                    "[NexGen] [{CorrelationId}] Posting completed. FilesProcessed: {Count}, ZipFilesCreated: {ZipCount}, DryRun: {DryRun}",
                    correlationId, response.FilesCount, response.ZipFilesCreated.Count, request.DryRun);
            }
            catch (Exception ex)
            {
                var msg = $"Unexpected error during NexGen file posting: {ex.Message}";
                _logger.LogError(ex, "[NexGen] [{CorrelationId}] {Message}", correlationId, msg);
                response.Success = false;
                response.Errors.Add(msg);
            }

            return response;
        }

        // -----------------------------------------------------------------------------------------
        // Core per-prefix processing — mirrors the for loop in the Unix scripts
        // -----------------------------------------------------------------------------------------

        private async Task ProcessFilePrefixAsync(
            string blobConnection,
            string prefix,
            string storeNumber,
            NexGenPostFilesRequest request,
            string logTime,
            string correlationId,
            NexGenPostFilesResponse response,
            CancellationToken cancellationToken)
        {
            _logger.LogInformation(
                "[NexGen] [{CorrelationId}] Processing prefix '{Prefix}' for store '{StoreNumber}'",
                correlationId, prefix, storeNumber);

            var serviceClient = new BlobServiceClient(blobConnection);
            var sourceContainer = serviceClient.GetBlobContainerClient(request.SourceContainer);

            // Find matching blobs — mirrors: ls -1 $SEARCHPATTERN
            // Pattern: TAR_ITM_{GM|PRPC}_{####}_{YYYYMMDD}_{HHMMSS}.txt
            var matchingBlobs = new List<BlobItem>();
            await foreach (var blob in sourceContainer.GetBlobsAsync(cancellationToken: cancellationToken))
            {
                if (IsMatchingFile(blob.Name, prefix, storeNumber))
                    matchingBlobs.Add(blob);
            }

            // Mirror Unix exit 2 per prefix: no files found
            if (!matchingBlobs.Any())
            {
                var searchPattern = storeNumber == "ALL"
                    ? $"{prefix}_????_????????_??????.txt"
                    : $"{prefix}_{storeNumber}_????????_??????.txt";

                var msg = $"No files found matching '{searchPattern}' in '{request.SourceContainer}'.";
                _logger.LogWarning("[NexGen] [{CorrelationId}] {Message}", correlationId, msg);
                response.Warnings.Add(msg);
                return;
            }

            _logger.LogInformation(
                "[NexGen] [{CorrelationId}] Found {Count} file(s) for prefix '{Prefix}'",
                correlationId, matchingBlobs.Count, prefix);

            foreach (var blobItem in matchingBlobs)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await ProcessSingleFileAsync(
                    blobConnection,
                    blobItem.Name,
                    prefix,
                    request,
                    logTime,
                    correlationId,
                    response,
                    cancellationToken);
            }
        }

        // -----------------------------------------------------------------------------------------
        // Per-file processing — mirrors the body of the for loop in the Unix scripts
        // -----------------------------------------------------------------------------------------

        private async Task ProcessSingleFileAsync(
            string blobConnection,
            string blobName,
            string prefix,
            NexGenPostFilesRequest request,
            string logTime,
            string correlationId,
            NexGenPostFilesResponse response,
            CancellationToken cancellationToken)
        {
            _logger.LogInformation(
                "[NexGen] [{CorrelationId}] Currently processing: {BlobName}",
                correlationId, blobName);

            // Extract filename parts from TAR_ITM_{prefix}_{storeNum}_{date}_{time}.txt
            // Mirrors: FNSTRNUM, FNDATE, FNTIME extraction via cut -d_ -f4,5,6
            var parts = Path.GetFileNameWithoutExtension(blobName).Split('_');
            // parts: [TAR, ITM, GM/PRPC, storeNum, date, time]
            if (parts.Length < 6)
            {
                var msg = $"File '{blobName}' has unexpected name format. Skipping.";
                _logger.LogWarning("[NexGen] [{CorrelationId}] {Message}", correlationId, msg);
                response.Warnings.Add(msg);
                return;
            }

            var fnStoreNum = parts[3];  // FNSTRNUM
            var fnDate = parts[4];      // FNDATE
            var fnTime = parts[5];      // FNTIME

            // Build zip file name — mirrors: POSTFN="$FNBEGIN_$FNSTRNUM_$FNDATE_$FNTIME.zip"
            var zipFileName = $"{prefix}_{fnStoreNum}_{fnDate}_{fnTime}.zip";

            // Build archive names — mirrors: SAVEDIR/$POSTFN.$LOGTIME and SAVEDIR/$WRKCPY
            var archiveZipName = $"{zipFileName}.{logTime}";
            var archiveOriginalName = $"{blobName}.{logTime}";

            _logger.LogInformation(
                "[NexGen] [{CorrelationId}] Store#: {StoreNum} | Date: {Date} | Time: {Time} | ZipFile: {ZipFile}",
                correlationId, fnStoreNum, fnDate, fnTime, zipFileName);

            var serviceClient = new BlobServiceClient(blobConnection);
            var sourceContainer = serviceClient.GetBlobContainerClient(request.SourceContainer);
            var destContainer = serviceClient.GetBlobContainerClient(request.DestinationContainer);
            var archiveContainer = serviceClient.GetBlobContainerClient(request.ArchiveContainer);

            // --- Stream source .txt file into memory ---
            // Mirrors: mv $ORIGFILE $WRKCPY then cp -p $WRKCPY $FILETOZIP
            // We read the blob as a stream — no disk I/O
            var sourceBlobClient = sourceContainer.GetBlobClient(blobName);

            using var sourceStream = new MemoryStream();
            await sourceBlobClient.DownloadToAsync(sourceStream, cancellationToken);
            sourceStream.Position = 0;

            _logger.LogInformation(
                "[NexGen] [{CorrelationId}] Downloaded source file '{BlobName}' ({Bytes} bytes)",
                correlationId, blobName, sourceStream.Length);

            // --- Zip in memory — mirrors: 7z a $POSTFN $FILETOZIP ---
            using var zipStream = new MemoryStream();
            using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create, leaveOpen: true))
            {
                // The entry inside the zip uses the original .txt filename
                // This matches 7z behaviour: zip contains the source file by name
                var entry = archive.CreateEntry(blobName, CompressionLevel.Optimal);
                using var entryStream = entry.Open();
                await sourceStream.CopyToAsync(entryStream, cancellationToken);
            }
            zipStream.Position = 0;

            _logger.LogInformation(
                "[NexGen] [{CorrelationId}] Zip created in memory: '{ZipFileName}' ({Bytes} bytes)",
                correlationId, zipFileName, zipStream.Length);

            if (request.DryRun)
            {
                _logger.LogInformation(
                    "[NexGen] [{CorrelationId}] DRY RUN — no files written. Would have posted '{ZipFileName}' to '{DestContainer}' and archived to '{ArchiveContainer}'.",
                    correlationId, zipFileName, request.DestinationContainer, request.ArchiveContainer);
                response.FilesProcessed.Add(blobName);
                response.ZipFilesCreated.Add($"[DRY RUN] {request.DestinationContainer}/{zipFileName}");
                response.ArchiveFilesCreated.Add($"[DRY RUN] {request.ArchiveContainer}/{archiveZipName}");
                return;
            }

            // --- Write backup copy of zip to archive container ---
            // Mirrors: cp -p $POSTFN $SAVEDIR/$POSTFN.$LOGTIME
            var archiveZipClient = archiveContainer.GetBlobClient(archiveZipName);
            zipStream.Position = 0;
            await archiveZipClient.UploadAsync(zipStream, overwrite: true, cancellationToken: cancellationToken);
            _logger.LogInformation(
                "[NexGen] [{CorrelationId}] Zip backup written to archive: '{ArchiveName}'",
                correlationId, archiveZipName);

            // --- Write zip to destination container (delivery folder) ---
            // Mirrors: mv $POSTFN $POSTDIR/.
            // If file already exists it is overwritten — mirrors the script's overwrite behaviour
            var destBlobClient = destContainer.GetBlobClient(zipFileName);
            var destExists = await destBlobClient.ExistsAsync(cancellationToken);
            if (destExists.Value)
            {
                _logger.LogWarning(
                    "[NexGen] [{CorrelationId}] '{ZipFileName}' already exists in destination. It will be overwritten.",
                    correlationId, zipFileName);
            }
            zipStream.Position = 0;
            await destBlobClient.UploadAsync(zipStream, overwrite: true, cancellationToken: cancellationToken);
            _logger.LogInformation(
                "[NexGen] [{CorrelationId}] File successfully posted as '{ZipFileName}' to '{DestContainer}'",
                correlationId, zipFileName, request.DestinationContainer);

            // --- Write original .txt backup to archive container ---
            // Mirrors: mv $WRKCPY $SAVEDIR/.
            sourceStream.Position = 0;
            var archiveOriginalClient = archiveContainer.GetBlobClient(archiveOriginalName);
            await archiveOriginalClient.UploadAsync(sourceStream, overwrite: true, cancellationToken: cancellationToken);
            _logger.LogInformation(
                "[NexGen] [{CorrelationId}] Original file saved to archive as '{ArchiveOriginalName}'",
                correlationId, archiveOriginalName);

            // Track in response
            response.FilesProcessed.Add(blobName);
            response.ZipFilesCreated.Add($"{request.DestinationContainer}/{zipFileName}");
            response.ArchiveFilesCreated.Add($"{request.ArchiveContainer}/{archiveZipName}");

            _logger.LogInformation(
                "[NexGen] [{CorrelationId}] Completed processing '{BlobName}'",
                correlationId, blobName);
        }

        // -----------------------------------------------------------------------------------------
        // Helpers
        // -----------------------------------------------------------------------------------------

        private List<string> ValidateRequest(NexGenPostFilesRequest request)
        {
            var errors = new List<string>();

            if (string.IsNullOrWhiteSpace(request.SourceContainer))
                errors.Add("SourceContainer is required.");

            if (string.IsNullOrWhiteSpace(request.DestinationContainer))
                errors.Add("DestinationContainer is required.");

            if (string.IsNullOrWhiteSpace(request.ArchiveContainer))
                errors.Add("ArchiveContainer is required.");

            if (string.IsNullOrWhiteSpace(request.ClientCode))
                errors.Add("ClientCode is required.");

            if (string.IsNullOrWhiteSpace(request.FileType))
            {
                errors.Add("FileType is required. Valid values: GM, PRPC, BOTH.");
            }
            else
            {
                var ft = request.FileType.Trim().ToUpperInvariant();
                if (ft != FileTypeGm && ft != FileTypePrpc && ft != FileTypeBoth)
                    errors.Add($"FileType '{request.FileType}' is invalid. Valid values: GM, PRPC, BOTH.");
            }

            if (string.IsNullOrWhiteSpace(request.StoreNumber))
            {
                errors.Add("StoreNumber is required. Use a 4-digit store number or 'ALL'.");
            }
            else
            {
                var sn = request.StoreNumber.Trim().ToUpperInvariant();
                // Mirror Unix validation: if [ ! ${#STRNUM} == "4" ]
                // "ALL" is normalised to "????" internally — but the caller passes "ALL"
                // Any specific store number must be exactly 4 characters (RJZF format)
                if (sn != "ALL" && sn.Length != 4)
                    errors.Add($"StoreNumber '{request.StoreNumber}' must be exactly 4 characters or 'ALL'. " +
                               $"Please enter as 4-digit RJZF format.");
            }

            return errors;
        }

        private List<string> ResolvePrefixes(string fileType)
        {
            return fileType switch
            {
                FileTypeGm => new List<string> { GmPrefix },
                FileTypePrpc => new List<string> { PrpcPrefix },
                FileTypeBoth => new List<string> { GmPrefix, PrpcPrefix },
                _ => new List<string>()
            };
        }

        /// <summary>
        /// Determines if a blob name matches the expected NexGen file pattern.
        /// Mirrors the glob pattern: $FNBEGIN_$STRNUM_????????_??????.txt
        /// When storeNumber is ALL, matches any 4-digit store number.
        /// </summary>
        private bool IsMatchingFile(string blobName, string prefix, string storeNumber)
        {
            var match = FileNamePattern.Match(blobName);
            if (!match.Success) return false;

            // Check prefix matches
            if (!blobName.StartsWith(prefix + "_", StringComparison.OrdinalIgnoreCase))
                return false;

            // If specific store number, check it matches — mirrors: STRNUM=$1 filter
            if (storeNumber != "ALL")
            {
                var fileStoreNum = match.Groups[2].Value;
                if (!string.Equals(fileStoreNum, storeNumber, StringComparison.OrdinalIgnoreCase))
                    return false;
            }

            return true;
        }

        private async Task<bool> ContainerExistsAsync(string blobConnection, string containerName)
        {
            var serviceClient = new BlobServiceClient(blobConnection);
            var container = serviceClient.GetBlobContainerClient(containerName);
            var exists = await container.ExistsAsync();
            return exists.Value;
        }

        private string BuildSearchPattern(List<string> prefixes, string storeNumber)
        {
            var sn = storeNumber.ToUpperInvariant() == "ALL" ? "????" : storeNumber;
            return string.Join(", ", prefixes.Select(p => $"{p}_{sn}_????????_??????.txt"));
        }
    }
}
