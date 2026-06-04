using Domain.ApiModels.Pewo;
using Domain.Services.Interfaces.Pewo;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace API.Controllers
{
    /// <summary>
    /// PEWO — Post-Event Workflow Orchestrator controller.
    ///
    /// All endpoints require X-Api-Key header (PewoApiKey from Key Vault).
    /// No heavy work runs here — all logic delegates to PewoWorkerService or PewoJobDataService.
    ///
    /// Endpoints:
    ///   POST  /api/pewo/worker/run                    — CronJob entry point
    ///   GET   /api/pewo/jobs/due                      — due schedules + retry runs
    ///   GET   /api/pewo/runs/{runId}                  — resume query
    ///   POST  /api/pewo/runs/{runId}/lock             — acquire/release schedule lock
    ///   POST  /api/pewo/runs/{runId}/steps/{stepId}   — upsert step status
    ///   POST  /api/pewo/runs/{runId}                  — set terminal run status
    ///   POST  /api/pewo/runs/{runId}/retry            — manual retry
    ///   POST  /api/pewo/totals-check                  — validate GM+PRPC files
    ///   GET   /api/pewo/health                        — liveness
    /// </summary>
    [ApiController]
    [Route("api/pewo")]
    public class PewoController : ControllerBase
    {
        private readonly IPewoWorkerService _workerService;
        private readonly IPewoJobDataService _jobDataService;
        private readonly IPewoTotalsCheckService _totalsCheckService;
        private readonly ILogger<PewoController> _logger;

        // Key Vault key name — matched by middleware / attribute
        private const string ApiKeyHeader = "X-Api-Key";
        private const string ApiKeyEnvVar = "PewoApiKey";

        public PewoController(
            IPewoWorkerService workerService,
            IPewoJobDataService jobDataService,
            IPewoTotalsCheckService totalsCheckService,
            ILogger<PewoController> logger)
        {
            _workerService      = workerService;
            _jobDataService     = jobDataService;
            _totalsCheckService = totalsCheckService;
            _logger             = logger;
        }

        // ── Auth helper ──────────────────────────────────────────────────────

        private IActionResult? ValidateApiKey()
        {
            var expectedKey = Environment.GetEnvironmentVariable(ApiKeyEnvVar);
            if (string.IsNullOrWhiteSpace(expectedKey))
            {
                _logger.LogError("[PEWO] {Env} environment variable not set", ApiKeyEnvVar);
                return StatusCode(500, "Server configuration error");
            }

            if (!Request.Headers.TryGetValue(ApiKeyHeader, out var providedKey) ||
                !string.Equals(providedKey, expectedKey, StringComparison.Ordinal))
            {
                _logger.LogWarning("[PEWO] Unauthorized request — invalid or missing {Header}", ApiKeyHeader);
                return Unauthorized(new { error = "Invalid or missing X-Api-Key" });
            }

            return null;
        }

        // ── POST /api/pewo/worker/run ────────────────────────────────────────
        /// <summary>
        /// CronJob entry point. Triggers PewoWorkerService.RunAsync().
        /// Returns when all due jobs for this wake are processed.
        /// </summary>
        [HttpPost("worker/run")]
        public async Task<IActionResult> WorkerRun(CancellationToken cancellationToken)
        {
            var auth = ValidateApiKey();
            if (auth != null) return auth;

            _logger.LogInformation("[PEWO] POST /api/pewo/worker/run");
            var result = await _workerService.RunAsync(cancellationToken);
            return Ok(result);
        }

        // ── GET /api/pewo/jobs/due ───────────────────────────────────────────
        /// <summary>Returns due Schedule rows + failed WorkflowRuns due for retry.</summary>
        [HttpGet("jobs/due")]
        public async Task<IActionResult> GetDueJobs(CancellationToken cancellationToken)
        {
            var auth = ValidateApiKey();
            if (auth != null) return auth;

            var result = await _jobDataService.GetDueJobsAsync(cancellationToken);
            return Ok(result);
        }

        // ── GET /api/pewo/runs/{runId} ───────────────────────────────────────
        /// <summary>Resume query — LEFT JOIN StepRun to WorkflowStepDef ordered by step_order.</summary>
        [HttpGet("runs/{runId}")]
        public async Task<IActionResult> GetRunResume(
            Guid runId,
            [FromQuery] int workflowTypeId,
            CancellationToken cancellationToken)
        {
            var auth = ValidateApiKey();
            if (auth != null) return auth;

            var result = await _jobDataService.GetRunResumeAsync(runId, workflowTypeId, cancellationToken);
            return Ok(result);
        }

        // ── POST /api/pewo/runs/{runId}/lock ─────────────────────────────────
        /// <summary>
        /// Atomic CAS acquire/release on Schedule.status.
        /// Body: { scheduleId, workerId, release?, nextRunAt?, lastRunId?, lastStatus? }
        /// </summary>
        [HttpPost("runs/{runId}/lock")]
        public async Task<IActionResult> Lock(Guid runId, [FromBody] LockRequest request, CancellationToken cancellationToken)
        {
            var auth = ValidateApiKey();
            if (auth != null) return auth;

            if (request.Release)
            {
                await _jobDataService.ReleaseScheduleLockAsync(
                    request.ScheduleId,
                    request.WorkerId,
                    request.NextRunAt ?? DateTime.UtcNow.AddHours(1),
                    request.LastRunId ?? runId,
                    request.LastStatus ?? "COMPLETED",
                    cancellationToken);
                return Ok(new { released = true });
            }

            var acquired = await _jobDataService.AcquireScheduleLockAsync(request.ScheduleId, request.WorkerId, cancellationToken);
            return Ok(new { acquired });
        }

        // ── POST /api/pewo/runs/{runId}/steps/{stepId} ───────────────────────
        /// <summary>Upsert StepRun status (SUCCESS/FAILED + reason + artifactRef). Called once after step completes.</summary>
        [HttpPost("runs/{runId}/steps/{stepId}")]
        public async Task<IActionResult> UpsertStep(
            Guid runId,
            int stepId,
            [FromBody] UpsertStepRequest request,
            CancellationToken cancellationToken)
        {
            var auth = ValidateApiKey();
            if (auth != null) return auth;

            await _jobDataService.UpsertStepRunAsync(
                runId,
                stepId,
                request.Status,
                request.Attempts,
                request.Reason,
                request.ArtifactRef,
                request.StartedAt,
                request.FinishedAt,
                cancellationToken);
            return Ok();
        }

        // ── POST /api/pewo/runs/{runId} ──────────────────────────────────────
        /// <summary>Set terminal job status. Advances Schedule.next_run_at.</summary>
        [HttpPost("runs/{runId}")]
        public async Task<IActionResult> SetRunStatus(
            Guid runId,
            [FromBody] SetRunStatusRequest request,
            CancellationToken cancellationToken)
        {
            var auth = ValidateApiKey();
            if (auth != null) return auth;

            await _jobDataService.SetRunTerminalStatusAsync(
                runId,
                request.Status,
                request.Reason,
                request.RetryAt,
                request.RetryCount,
                cancellationToken);
            return Ok();
        }

        // ── POST /api/pewo/runs/{runId}/retry ────────────────────────────────
        /// <summary>Manual retry — reset FAILED StepRun rows to PENDING. Leave SUCCESS untouched.</summary>
        [HttpPost("runs/{runId}/retry")]
        public async Task<IActionResult> RetryRun(Guid runId, CancellationToken cancellationToken)
        {
            var auth = ValidateApiKey();
            if (auth != null) return auth;

            await _jobDataService.ResetRunForRetryAsync(runId, cancellationToken);
            return Ok(new { runId, queued = true });
        }

        // ── POST /api/pewo/totals-check ──────────────────────────────────────
        /// <summary>Validate GM+PRPC source files. Returns { passed, reason, filesChecked }.</summary>
        [HttpPost("totals-check")]
        public async Task<IActionResult> TotalsCheck([FromBody] TotalsCheckRequest request, CancellationToken cancellationToken)
        {
            var auth = ValidateApiKey();
            if (auth != null) return auth;

            var result = await _totalsCheckService.CheckAsync(request, cancellationToken);
            return Ok(result);
        }

        // ── GET /api/pewo/health ─────────────────────────────────────────────
        /// <summary>Liveness check. No auth required.</summary>
        [HttpGet("health")]
        public IActionResult Health()
            => Ok(new PewoHealthResponse { Status = "ok", Timestamp = DateTime.UtcNow });
    }

    // ── Inline request models (thin controller — no separate model file needed) ─

    public class LockRequest
    {
        public int ScheduleId { get; set; }
        public string WorkerId { get; set; } = string.Empty;
        public bool Release { get; set; }
        public DateTime? NextRunAt { get; set; }
        public Guid? LastRunId { get; set; }
        public string? LastStatus { get; set; }
    }

    public class UpsertStepRequest
    {
        public string Status { get; set; } = string.Empty;
        public int Attempts { get; set; }
        public string? Reason { get; set; }
        public string? ArtifactRef { get; set; }
        public DateTime? StartedAt { get; set; }
        public DateTime? FinishedAt { get; set; }
    }

    public class SetRunStatusRequest
    {
        public string Status { get; set; } = string.Empty;
        public string? Reason { get; set; }
        public DateTime? RetryAt { get; set; }
        public int RetryCount { get; set; }
    }
}
