using Domain.Constants;
using Domain.Helpers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Logging;
using System;

namespace API.Filters;

/// <summary>
/// Machine-to-machine API key filter for POST /api/Pewo/worker/run.
/// Validates X-Api-Key header against PewoApiKey secret in Key Vault.
/// Applied via [TypeFilter(typeof(PewoApiKeyFilter))].
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public class PewoApiKeyFilter : Attribute, IActionFilter
{
    private const string ApiKeyHeader = "X-Api-Key";

    private readonly ILogger<PewoApiKeyFilter> _logger;

    public PewoApiKeyFilter(ILogger<PewoApiKeyFilter> logger)
    {
        _logger = logger;
    }

    public void OnActionExecuting(ActionExecutingContext context)
    {
        try
        {
            if (!context.HttpContext.Request.Headers.TryGetValue(ApiKeyHeader, out var providedKey)
                || string.IsNullOrWhiteSpace(providedKey))
            {
                _logger.LogWarning("[PEWO] Unauthorized — {Header} header missing or empty", ApiKeyHeader);
                context.Result = new UnauthorizedObjectResult(
                    new { error = MessageConstants.InvalidAccessToken });
                return;
            }

            var expectedKey = KeyVaultSecretManager.GetSecretValueBySecretKey(WISAppConstants.PewoApiKey);

            if (string.IsNullOrWhiteSpace(expectedKey) ||
                !string.Equals(providedKey, expectedKey, StringComparison.Ordinal))
            {
                _logger.LogWarning("[PEWO] Unauthorized — {Header} value mismatch", ApiKeyHeader);
                context.Result = new UnauthorizedObjectResult(
                    new { error = MessageConstants.InvalidAccessToken });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[PEWO] Error validating API key");
            context.Result = new UnauthorizedObjectResult(
                new { error = MessageConstants.InvalidAccessToken });
        }
    }

    public void OnActionExecuted(ActionExecutedContext context) { }
}
