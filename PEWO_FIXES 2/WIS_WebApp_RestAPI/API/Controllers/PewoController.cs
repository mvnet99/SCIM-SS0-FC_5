using Domain.ApiModels.Pewo;
using Domain.Services.Interfaces.Pewo;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System.Threading;
using System.Threading.Tasks;

namespace API.Controllers;

/// <summary>
/// PEWO — Post-Event Workflow Orchestrator.
/// Fix 14: IPewoLogService injected — RetryRun writes audit log row to Pewo_WorkflowRunLog.
///         Allows SRE to see full history of manual retry interventions.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class PewoController : Controller
{
    private readonly IPewoWorkerService      _pewoWorkerService;
    private readonly IPewoStepService        _pewoStepService;
    private readonly IPewoJobDataService     _pewoJobDataService;
    private readonly IPewoLogService         _pewoLogService;       // Fix 14
    private readonly ILogger<PewoController> _logger;

    public PewoController(
        IPewoWorkerService      pewoWorkerService,
        IPewoStepService        pewoStepService,
        IPewoJobDataService     pewoJobDataService,
        IPewoLogService         pewoLogService,                     // Fix 14
        ILogger<PewoController> logger)
    {
        _pewoWorkerService  = pewoWorkerService;
        _pewoStepService    = pewoStepService;
        _pewoJobDataService = pewoJobDataService;
        _pewoLogService     = pewoLogService;
        _logger             = logger;
    }

    /// <summary>Manual trigger — same method Container App Job calls automatically every interval.</summary>
    [HttpPost("worker/run")]
    [TypeFilter(typeof(Filters.PewoApiKeyFilter))]
    [ProducesResponseType(typeof(PewoWorkerRunResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult> WorkerRun(CancellationToken cancellationToken)
    {
        _logger.LogInformation("[PEWO] POST worker/run triggered");
        var result = await _pewoWorkerService.RunAsync(cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// Called by EventService.CloseInventory when event closes.
    /// Primes ON_EVENT_CLOSE workflows by creating WorkflowRun + WorkflowRunEvent rows.
    /// </summary>
    [HttpPost("event-close")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult> EventClose(
        [FromBody] PewoEventCloseRequest request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("[PEWO] event-close Customer={C} Event={E} Store={S}",
            request.Id_Customer, request.Id_Event, request.Store_No);

        var result = await _pewoJobDataService.CreateRunOnEventCloseAsync(
            request.Id_Customer, request.Id_Event,
            request.Store_No, request.Store_Name, request.Event_Date,
            request.WorkflowType_Code, request.Event_Guid, request.Id_Store,
            request.Event_Status, request.Event_Scheduled_Date,
            cancellationToken);

        return Ok(result);
    }

    [HttpPost("steps/totals-check")]
    [ProducesResponseType(typeof(PewoStepResponse), StatusCodes.Status200OK)]
    public ActionResult TotalsCheck([FromBody] PewoStepRequest request)
    {
        var result = _pewoStepService.TotalsCheck(request);
        return Ok(result);
    }

    [HttpPost("steps/read-blob-zip")]
    [ProducesResponseType(typeof(PewoStepResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult> ReadBlobZip(
        [FromBody] PewoStepRequest request, CancellationToken cancellationToken)
    {
        var result = await _pewoStepService.ReadBlobZipAsync(request, cancellationToken);
        return Ok(result);
    }

    [HttpPost("steps/sftp")]
    [ProducesResponseType(typeof(PewoStepResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult> Sftp(
        [FromBody] PewoStepRequest request, CancellationToken cancellationToken)
    {
        var result = await _pewoStepService.SftpAsync(request, cancellationToken);
        return Ok(result);
    }

    [HttpPost("steps/archive")]
    [ProducesResponseType(typeof(PewoStepResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult> Archive(
        [FromBody] PewoStepRequest request, CancellationToken cancellationToken)
    {
        var result = await _pewoStepService.ArchiveAsync(request, cancellationToken);
        return Ok(result);
    }

    [HttpPost("steps/email")]
    [ProducesResponseType(typeof(PewoStepResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult> Email(
        [FromBody] PewoStepRequest request, CancellationToken cancellationToken)
    {
        var result = await _pewoStepService.EmailAsync(request, cancellationToken);
        return Ok(result);
    }

    [HttpPost("steps/email-summary")]
    [ProducesResponseType(typeof(PewoStepResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult> EmailSummary(
        [FromBody] PewoStepRequest request, CancellationToken cancellationToken)
    {
        var result = await _pewoStepService.EmailSummaryAsync(request, cancellationToken);
        return Ok(result);
    }

    [HttpPost("steps/get-events")]
    [ProducesResponseType(typeof(PewoStepResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult> GetEvents(
        [FromBody] PewoStepRequest request, CancellationToken cancellationToken)
    {
        var result = await _pewoStepService.GetEventsAsync(request, cancellationToken);
        return Ok(result);
    }

    [HttpPost("steps/transform")]
    [ProducesResponseType(typeof(PewoStepResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult> Transform(
        [FromBody] PewoStepRequest request, CancellationToken cancellationToken)
    {
        var result = await _pewoStepService.TransformAsync(request, cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// Resets FAILED steps to PENDING on a run — COMPLETED steps untouched (resume-not-restart).
    /// Also resets Retry_Count to 0 so the run can be picked up by the worker again.
    /// Fix 14: Writes audit log row to Pewo_WorkflowRunLog for SRE visibility.
    ///         Use this to investigate and resolve permanently failed runs.
    /// </summary>
    [HttpPost("runs/{id}/retry")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult> RetryRun(int id, CancellationToken cancellationToken)
    {
        _logger.LogInformation("[PEWO] POST runs/{Id}/retry — manual retry initiated by operator", id);

        await _pewoJobDataService.ResetRunForRetryAsync(id, cancellationToken);

        // Fix 14: Audit log — operator visibility into manual retry history
        // Audit log is also written inside usp_Pewo_ResetRunForRetry SP.
        // This controller-level log provides additional context in application logs.
        await _pewoLogService.LogAsync(
            id, null, null, null,
            "INFO",
            $"Manual retry initiated via POST /api/Pewo/runs/{id}/retry. " +
            "Retry_Count reset to 0. FAILED steps reset to PENDING. COMPLETED steps untouched.",
            null, cancellationToken);

        return Ok();
    }
}
