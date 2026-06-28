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
///
/// All business logic lives in PewoWorkerService and PewoStepService,
/// which the BackgroundService calls automatically. These endpoints expose
/// the same logic as HTTP for testing, manual operations, and ops tooling.
///
/// Endpoints:
///   POST /api/Pewo/worker/run               — Manual trigger (BackgroundService calls automatically)
///   POST /api/Pewo/event-close              — Prime schedule when an inventory event closes
///   POST /api/Pewo/steps/totals-check       — GM/PRPC totals validation (Day 1)
///   POST /api/Pewo/steps/read-blob-zip      — Read source files and create individual zips (Day 2)
///   POST /api/Pewo/steps/sftp               — SFTP delivery to TARGET (Day 2)
///   POST /api/Pewo/steps/archive            — Archive originals to flexcount-save (Day 2)
///   POST /api/Pewo/steps/email              — Per-event notification email
///   POST /api/Pewo/steps/email-summary      — Consolidated batch ack email
///   POST /api/Pewo/steps/get-events         — MEO event discovery (stub)
///   POST /api/Pewo/steps/transform          — MEO file conversion (stub)
///   POST /api/Pewo/runs/{id}/retry          — Manual retry reset
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class PewoController : Controller
{
    private readonly IPewoWorkerService  _pewoWorkerService;
    private readonly IPewoStepService    _pewoStepService;
    private readonly IPewoJobDataService _pewoJobDataService;
    private readonly ILogger<PewoController> _logger;

    public PewoController(
        IPewoWorkerService   pewoWorkerService,
        IPewoStepService     pewoStepService,
        IPewoJobDataService  pewoJobDataService,
        ILogger<PewoController> logger)
    {
        _pewoWorkerService  = pewoWorkerService;
        _pewoStepService    = pewoStepService;
        _pewoJobDataService = pewoJobDataService;
        _logger             = logger;
    }

    /// <summary>
    /// Manual trigger — same method BackgroundService calls automatically every interval.
    /// Protected by PewoApiKeyFilter (X-Api-Key header, validated against Key Vault).
    /// </summary>
    [HttpPost("worker/run")]
    [TypeFilter(typeof(Filters.PewoApiKeyFilter))]
    [ProducesResponseType(typeof(PewoWorkerRunResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult> WorkerRun(CancellationToken cancellationToken)
    {
        _logger.LogInformation("[PEWO] POST worker/run triggered manually");
        var result = await _pewoWorkerService.RunAsync(cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// Called by existing event-close flow. Primes GM_TOTALS_CHECK (and optionally other workflows)
    /// by creating WorkflowRun + WorkflowRunEvent rows and advancing schedule.Next_Run_At to now.
    /// Idempotent: duplicate calls for same event_Guid create no duplicates (SP NOT EXISTS guard).
    /// </summary>
    [HttpPost("event-close")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
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
    /// Resets FAILED steps to PENDING on a run, leaves COMPLETED steps untouched.
    /// Same resume-not-restart logic — worker picks it up on next tick.
    /// </summary>
    [HttpPost("runs/{id}/retry")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult> RetryRun(int id, CancellationToken cancellationToken)
    {
        _logger.LogInformation("[PEWO] POST runs/{Id}/retry", id);
        await _pewoJobDataService.ResetRunForRetryAsync(id, cancellationToken);
        return Ok();
    }
}
