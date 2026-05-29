using Domain.ApiModels;
using Domain.Constants;
using Domain.Services.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace API.Controllers
{
    /// <summary>
    /// NexGen file posting endpoint.
    /// Called by Logic Apps (system-to-system) to zip and deliver
    /// TAR_ITM_GM and TAR_ITM_PRPC NexGen files from Blob to the delivery container.
    ///
    /// Auth: API key via X-Api-Key header (validated against Key Vault secret).
    /// This controller intentionally does NOT use AuthorizationFilterAttribute
    /// because this is a system-to-system call with no event user context.
    /// </summary>
    [Route("api/[controller]")]
    [ApiController]
    public class NexGenController : ControllerBase
    {
        private readonly INexGenService _nexGenService;
        private readonly IConfiguration _configuration;
        private readonly ILogger<NexGenController> _logger;

        // Key Vault secret name for the API key — add this to KeyVaultSettings in appsettings.json
        private const string ApiKeySecretName = "NexGenApiKey";

        public NexGenController(
            INexGenService nexGenService,
            IConfiguration configuration,
            ILogger<NexGenController> logger)
        {
            _nexGenService = nexGenService;
            _configuration = configuration;
            _logger = logger;
        }

        /// <summary>
        /// Posts NexGen GM and/or PRPC files from Blob storage.
        /// Streams source .txt files, zips in memory, writes to delivery container,
        /// and archives originals. Called by Logic Apps on a schedule (Wed/Fri before 9:30 AM EST).
        /// </summary>
        /// <param name="request">Post files request parameters</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>NexGenPostFilesResponse with files processed and any errors</returns>
        [HttpPost("PostFiles")]
        [ProducesResponseType(typeof(NexGenPostFilesResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(NexGenPostFilesResponse), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<ActionResult<NexGenPostFilesResponse>> PostFiles(
            [FromBody] NexGenPostFilesRequest request,
            CancellationToken cancellationToken)
        {
            // --- API Key validation ---
            // Logic Apps passes this in the HTTP action header as X-Api-Key
            if (!IsApiKeyValid())
            {
                _logger.LogWarning("[NexGen] Unauthorized request to PostFiles — invalid or missing API key.");
                return Unauthorized();
            }

            // --- Null body guard ---
            if (request == null)
            {
                return BadRequest(new NexGenPostFilesResponse
                {
                    Success = false,
                    Errors = { "Request body is required." }
                });
            }

            _logger.LogInformation(
                "[NexGen] PostFiles called. FileType: {FileType}, StoreNumber: {StoreNumber}, ClientCode: {ClientCode}, DryRun: {DryRun}",
                request.FileType, request.StoreNumber, request.ClientCode, request.DryRun);

            try
            {
                var result = await _nexGenService.PostNexGenFilesAsync(request, cancellationToken);

                if (!result.Success)
                {
                    // Return 400 if validation failed (bad inputs), 200 if processed but partial warnings
                    // If errors are validation-only, return BadRequest so Logic Apps can fail fast
                    bool isValidationFailure = result.FilesProcessed.Count == 0 && result.Errors.Count > 0;
                    if (isValidationFailure)
                        return BadRequest(result);
                }

                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[NexGen] Unhandled exception in PostFiles.");
                return StatusCode(StatusCodes.Status500InternalServerError, new NexGenPostFilesResponse
                {
                    Success = false,
                    CorrelationId = request.CorrelationId,
                    Errors = { $"Internal server error: {ex.Message}" }
                });
            }
        }

        // -----------------------------------------------------------------------------------------
        // API Key validation
        // Reads the expected key from configuration (loaded from Key Vault at startup).
        // Logic Apps passes X-Api-Key header in the HTTP action.
        // -----------------------------------------------------------------------------------------

        private bool IsApiKeyValid()
        {
            // Header name Logic Apps will send
            if (!Request.Headers.TryGetValue("X-Api-Key", out var providedKey))
                return false;

            // Key Vault loads this into configuration at startup via KeyVaultSettings
            var expectedKey = _configuration[ApiKeySecretName];

            if (string.IsNullOrWhiteSpace(expectedKey))
            {
                _logger.LogError("[NexGen] NexGenApiKey is not configured in Key Vault. Rejecting request.");
                return false;
            }

            // Constant-time comparison to prevent timing attacks
            return string.Equals(
                providedKey.ToString().Trim(),
                expectedKey.Trim(),
                StringComparison.Ordinal);
        }
    }
}
