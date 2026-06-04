using Microsoft.AspNetCore.Mvc;
using WS.FC.Dapper.Domain.Interfaces.Services;
using WS.FC.Dapper.Shared.DTOs.Pewo;

namespace WS.FC.Dapper.WebAPI.Controllers
{
    /// <summary>
    /// Database API endpoints consumed by the PEWO worker via PewoJobDataService.
    /// All routes under /api/PewoData/
    /// </summary>
    [Route("api/[controller]")]
    [ApiController]
    public class PewoDataController : ControllerBase
    {
        private readonly IPewoDataService _pewoDataService;

        public PewoDataController(IPewoDataService pewoDataService)
        {
            _pewoDataService = pewoDataService;
        }

        /// <summary>Returns Schedule rows due to fire + failed runs due for auto-retry.</summary>
        [HttpGet("due-jobs")]
        public async Task<IActionResult> GetDueJobs(CancellationToken cancellationToken)
        {
            var result = await _pewoDataService.GetDueJobsAsync(cancellationToken);
            return Ok(result);
        }

        /// <summary>Resume query — LEFT JOIN StepRun to WorkflowStepDef ordered by step_order.</summary>
        [HttpGet("runs/{runId}/resume")]
        public async Task<IActionResult> GetRunResume(
            Guid runId,
            [FromQuery] int workflowTypeId,
            CancellationToken cancellationToken)
        {
            var result = await _pewoDataService.GetRunResumeAsync(runId, workflowTypeId, cancellationToken);
            return Ok(result);
        }

        /// <summary>Atomic CAS acquire on Schedule.status ACTIVE → RUNNING.</summary>
        [HttpPost("schedules/{scheduleId}/acquire-lock")]
        public async Task<IActionResult> AcquireScheduleLock(
            int scheduleId,
            [FromBody] AcquireLockRequestDto request,
            CancellationToken cancellationToken)
        {
            var acquired = await _pewoDataService.AcquireScheduleLockAsync(scheduleId, request.WorkerId, cancellationToken);
            return Ok(new LockResultDto { Acquired = acquired });
        }

        /// <summary>Release schedule lock and advance next_run_at.</summary>
        [HttpPost("schedules/{scheduleId}/release-lock")]
        public async Task<IActionResult> ReleaseScheduleLock(
            int scheduleId,
            [FromBody] ReleaseLockRequestDto request,
            CancellationToken cancellationToken)
        {
            await _pewoDataService.ReleaseScheduleLockAsync(
                scheduleId,
                request.WorkerId,
                request.NextRunAt,
                request.LastRunId,
                request.LastStatus,
                cancellationToken);
            return Ok();
        }

        /// <summary>Create a new WorkflowRun row (PENDING).</summary>
        [HttpPost("runs")]
        public async Task<IActionResult> CreateWorkflowRun(
            [FromBody] CreateWorkflowRunRequestDto request,
            CancellationToken cancellationToken)
        {
            await _pewoDataService.CreateWorkflowRunAsync(
                request.RunId,
                request.WorkflowTypeId,
                request.ScheduleId,
                request.MaxRetries,
                cancellationToken);
            return Ok();
        }

        /// <summary>Upsert StepRun status (SUCCESS | FAILED). Called once after step completes.</summary>
        [HttpPost("runs/{runId}/steps/{stepDefId}")]
        public async Task<IActionResult> UpsertStepRun(
            Guid runId,
            int stepDefId,
            [FromBody] UpsertStepRunRequestDto request,
            CancellationToken cancellationToken)
        {
            await _pewoDataService.UpsertStepRunAsync(
                runId,
                stepDefId,
                request.Status,
                request.Attempts,
                request.Reason,
                request.ArtifactRef,
                request.StartedAt,
                request.FinishedAt,
                cancellationToken);
            return Ok();
        }

        /// <summary>Set terminal run status (COMPLETED | FAILED) and optionally set retry_at.</summary>
        [HttpPost("runs/{runId}/status")]
        public async Task<IActionResult> SetRunTerminalStatus(
            Guid runId,
            [FromBody] SetRunStatusRequestDto request,
            CancellationToken cancellationToken)
        {
            await _pewoDataService.SetRunTerminalStatusAsync(
                runId,
                request.Status,
                request.Reason,
                request.RetryAt,
                request.RetryCount,
                cancellationToken);
            return Ok();
        }

        /// <summary>Manual retry — reset FAILED StepRun rows to PENDING.</summary>
        [HttpPost("runs/{runId}/reset-for-retry")]
        public async Task<IActionResult> ResetRunForRetry(
            Guid runId,
            CancellationToken cancellationToken)
        {
            await _pewoDataService.ResetRunForRetryAsync(runId, cancellationToken);
            return Ok();
        }
    }
}
