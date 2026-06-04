using Azure.Storage.Blobs;
using Domain.ApiModels.Pewo;
using Domain.Services.Interfaces.Pewo;
using Microsoft.Extensions.Logging;

namespace Domain.Services.Pewo
{
    /// <summary>
    /// Validates GM + PRPC source files in fc-hold-target.
    /// Replaces TargetTotalsCheck.exe.
    ///
    /// TODO (@Rajesh Kumar): Port business rules from TargetTotalsCheck.exe.
    /// Current implementation validates file presence only.
    /// </summary>
    public class PewoTotalsCheckService : IPewoTotalsCheckService
    {
        private readonly BlobServiceClient _blobServiceClient;
        private readonly ILogger<PewoTotalsCheckService> _logger;

        public PewoTotalsCheckService(
            BlobServiceClient blobServiceClient,
            ILogger<PewoTotalsCheckService> logger)
        {
            _blobServiceClient = blobServiceClient;
            _logger = logger;
        }

        public async Task<TotalsCheckResponse> CheckAsync(TotalsCheckRequest request, CancellationToken cancellationToken)
        {
            _logger.LogInformation(
                "[PEWO] TotalsCheck starting. RunId={RunId} Store={Store} Container={Container}",
                request.RunId, request.StoreNo, request.SourceContainer);

            try
            {
                var containerClient = _blobServiceClient.GetBlobContainerClient(request.SourceContainer);
                var filesFound = new List<string>();

                // Enumerate blobs matching GM pattern
                await foreach (var blob in containerClient.GetBlobsAsync(prefix: request.FilePatternGm, cancellationToken: cancellationToken))
                    filesFound.Add(blob.Name);

                // Enumerate blobs matching PRPC pattern
                await foreach (var blob in containerClient.GetBlobsAsync(prefix: request.FilePatternPrpc, cancellationToken: cancellationToken))
                    filesFound.Add(blob.Name);

                if (filesFound.Count == 0)
                {
                    return new TotalsCheckResponse
                    {
                        Passed       = false,
                        Reason       = $"No files found matching GM pattern '{request.FilePatternGm}' or PRPC pattern '{request.FilePatternPrpc}' in '{request.SourceContainer}'.",
                        FilesChecked = 0
                    };
                }

                bool hasGm   = filesFound.Any(f => f.Contains(request.FilePatternGm,   StringComparison.OrdinalIgnoreCase));
                bool hasPrpc = filesFound.Any(f => f.Contains(request.FilePatternPrpc, StringComparison.OrdinalIgnoreCase));

                if (!hasGm || !hasPrpc)
                {
                    return new TotalsCheckResponse
                    {
                        Passed       = false,
                        Reason       = $"Missing files: GM={hasGm}, PRPC={hasPrpc}. Files found: {string.Join(", ", filesFound)}",
                        FilesChecked = filesFound.Count
                    };
                }

                // TODO (@Rajesh Kumar): Port TargetTotalsCheck.exe business rules here.
                // E.g. validate record counts, control totals, file format, trailer sums, etc.
                // For now: presence of both file types == PASSED.

                _logger.LogInformation(
                    "[PEWO] TotalsCheck PASSED. RunId={RunId} FilesChecked={Count}",
                    request.RunId, filesFound.Count);

                return new TotalsCheckResponse
                {
                    Passed       = true,
                    Reason       = "All required files present. Awaiting detailed totals validation (TODO: @Rajesh Kumar).",
                    FilesChecked = filesFound.Count
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[PEWO] TotalsCheck error. RunId={RunId}", request.RunId);
                return new TotalsCheckResponse
                {
                    Passed       = false,
                    Reason       = $"Exception during totals check: {ex.Message}",
                    FilesChecked = 0
                };
            }
        }
    }
}
