using Cronos;
using Domain.ApiModels.Pewo;
using Domain.Services.Interfaces.Pewo;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WS.FC.Dapper.Shared.DTOs.Pewo;

namespace Domain.Services.Pewo;

/// <summary>
/// PEWO job loop. Entry point: POST /api/Pewo/worker/run.
///
/// Per due job:
///   SCHEDULE source  → create new WorkflowRun
///   RETRY / NEW_CHILD → reuse existing id_WorkflowRun
///   Load WorkflowRunEvent once → extract Event_Guid, Store_No, Event_Date
///   Load steps via resume query — ordered, COMPLETED skipped (resume-not-restart)
///   Dispatch each step in-process via IPewoStepService
///   Persist result via UpsertStepRunAsync + log row after each step
///   Step failure → stop, compute exponential backoff, set FAILED terminal status
///   All steps success → set COMPLETED terminal status, advance schedule
/// </summary>
public class PewoWorkerService : IPewoWorkerService
{
    private const string EventPrimedToken = "EVENT_PRIMED";

    private readonly IPewoJobDataService       _jobDataService;
    private readonly IPewoLogService           _logService;
    private readonly IPewoStepService          _stepService;
    private readonly ILogger<PewoWorkerService> _logger;

    public PewoWorkerService(
        IPewoJobDataService        jobDataService,
        IPewoLogService            logService,
        IPewoStepService           stepService,
        ILogger<PewoWorkerService> logger)
    {
        _jobDataService = jobDataService;
        _logService     = logService;
        _stepService    = stepService;
        _logger         = logger;
    }

    public async Task<PewoWorkerRunResponse> RunAsync(CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        _logger.LogInformation("[PEWO] Worker run started at {Time}", DateTime.UtcNow);

        var dueJobs = await _jobDataService.GetDueJobsAsync(cancellationToken);
        _logger.LogInformation("[PEWO] Due jobs: {Count}", dueJobs.Count);

        int succeeded = 0, failed = 0;

        foreach (var job in dueJobs)
        {
            int runId = 0;
            try
            {
                // SCHEDULE source → new run; RETRY / NEW_CHILD → reuse existing
                runId = job.Job_Source == "SCHEDULE"
                    ? await _jobDataService.CreateWorkflowRunAsync(
                        job.Id_Schedule, job.Id_CustomerWorkflowType, job.Max_Retries, cancellationToken)
                    : job.Id_WorkflowRun!.Value;

                _logger.LogInformation("[PEWO] Processing RunId={RunId} Workflow={Code} Source={Source}",
                    runId, job.WorkflowType_Code, job.Job_Source);

                await _logService.LogAsync(runId, null, null, null, "INFO",
                    $"Job picked up. WorkflowCode={job.WorkflowType_Code} Source={job.Job_Source}",
                    null, cancellationToken);

                var runResult = await ExecuteStepsAsync(job, runId, cancellationToken);

                DateTime? retryAt   = null;
                short retryCount    = runResult.RetryCount;

                if (!runResult.Succeeded && retryCount < job.Max_Retries)
                {
                    retryAt = DateTime.UtcNow.AddMinutes((int)Math.Pow(2, retryCount));
                }

                await _jobDataService.SetRunTerminalStatusAsync(
                    runId,
                    runResult.Succeeded ? "COMPLETED" : "FAILED",
                    runResult.FailureReason,
                    retryAt,
                    retryCount,
                    cancellationToken);

                var nextRunAt = CalculateNextRunAt(job.Cron_Expression, job.Timezone);

                await _jobDataService.AdvanceScheduleAsync(
                    job.Id_Schedule, nextRunAt, runId,
                    runResult.Succeeded ? "COMPLETED" : "FAILED",
                    cancellationToken);

                await _logService.LogAsync(runId, null, null, null,
                    runResult.Succeeded ? "INFO" : "ERROR",
                    runResult.Succeeded ? "Run COMPLETED" : $"Run FAILED — {runResult.FailureReason}",
                    null, cancellationToken);

                if (runResult.Succeeded) succeeded++; else failed++;
            }
            catch (Exception ex)
            {
                failed++;
                _logger.LogError(ex, "[PEWO] Unhandled exception RunId={RunId}", runId);

                if (runId > 0)
                {
                    try
                    {
                        await _jobDataService.SetRunTerminalStatusAsync(
                            runId, "FAILED", $"Unhandled exception: {ex.Message}",
                            null, 0, cancellationToken);
                    }
                    catch (Exception inner)
                    {
                        _logger.LogError(inner, "[PEWO] Failed to set terminal status RunId={RunId}", runId);
                    }
                }
            }
        }

        sw.Stop();
        _logger.LogInformation("[PEWO] Worker run complete. Jobs={Total} OK={Ok} Failed={Fail} {Ms}ms",
            dueJobs.Count, succeeded, failed, sw.ElapsedMilliseconds);

        return new PewoWorkerRunResponse
        {
            JobsProcessed = dueJobs.Count,
            JobsSucceeded = succeeded,
            JobsFailed    = failed,
            DurationMs    = sw.ElapsedMilliseconds.ToString()
        };
    }

    // ── Private ───────────────────────────────────────────────────────────────

    private async Task<StepExecutionResult> ExecuteStepsAsync(
        DueJobDto job, int runId, CancellationToken cancellationToken)
    {
        var steps  = await _jobDataService.GetRunResumeAsync(runId, job.Id_CustomerWorkflowType, cancellationToken);
        var events = await _jobDataService.GetWorkflowRunEventsAsync(runId, cancellationToken);

        // Extract event context from the first populated WorkflowRunEvent row
        var firstEvent = events.FirstOrDefault();
        var eventGuid  = events.FirstOrDefault(e => e.Event_Guid.HasValue)?.Event_Guid?.ToString();
        var storeNo    = events.FirstOrDefault(e => !string.IsNullOrEmpty(e.Store_No))?.Store_No;
        var eventDate  = events.FirstOrDefault(e => e.Event_Date.HasValue)?.Event_Date;

        foreach (var step in steps.OrderBy(s => s.Step_Order))
        {
            if (step.Status == "COMPLETED")
            {
                _logger.LogInformation("[PEWO] RunId={RunId} Step={Kind} already COMPLETED — skipping",
                    runId, step.Step_Kind);
                continue;
            }

            var stepRequest = new PewoStepRequest
            {
                Id_WorkflowRun     = runId,
                Id_WorkflowStepDef = step.Id_WorkflowStepDef,
                Step_Kind          = step.Step_Kind,
                Config             = step.Config,
                Artifact_Ref       = step.Artifact_Ref,
                Attempts           = (short)(step.Attempts + 1),
                Max_Attempts       = step.Max_Attempts,
                Event_Guid         = eventGuid,
                Store_No           = storeNo,
                Event_Date         = eventDate
            };

            var startTime = DateTime.UtcNow;
            PewoStepResponse stepResponse;

            try
            {
                stepResponse = step.Step_Kind switch
                {
                    "TOTALS_CHECK"  => _stepService.TotalsCheck(stepRequest),
                    "GET_EVENTS"    => await _stepService.GetEventsAsync(stepRequest, cancellationToken),
                    "TRANSFORM"     => await _stepService.TransformAsync(stepRequest, cancellationToken),
                    "READ_BLOB_ZIP" => await _stepService.ReadBlobZipAsync(stepRequest, cancellationToken),
                    "SFTP"          => await _stepService.SftpAsync(stepRequest, cancellationToken),
                    "ARCHIVE"       => await _stepService.ArchiveAsync(stepRequest, cancellationToken),
                    "EMAIL"         => await _stepService.EmailAsync(stepRequest, cancellationToken),
                    "EMAIL_SUMMARY" => await _stepService.EmailSummaryAsync(stepRequest, cancellationToken),
                    _ => throw new InvalidOperationException($"Unknown Step_Kind: {step.Step_Kind}")
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[PEWO] RunId={RunId} Step={Kind} unhandled exception",
                    runId, step.Step_Kind);
                stepResponse = new PewoStepResponse
                {
                    Success         = false,
                    Failure_Details = $"Unhandled exception: {ex.Message}"
                };
            }

            var endTime   = DateTime.UtcNow;
            var newStatus = stepResponse.Success ? "COMPLETED" : "FAILED";

            await _jobDataService.UpsertStepRunAsync(
                runId, step.Id_WorkflowStepDef, step.Step_Kind,
                newStatus, stepRequest.Attempts,
                stepResponse.Artifact_Ref, stepResponse.Failure_Details,
                startTime, endTime, cancellationToken);

            await _logService.LogAsync(runId, null, storeNo, step.Step_Kind,
                stepResponse.Success ? "INFO" : "ERROR",
                stepResponse.Success
                    ? $"Step {step.Step_Kind} COMPLETED"
                    : $"Step {step.Step_Kind} FAILED: {stepResponse.Failure_Details}",
                eventGuid, cancellationToken);

            if (!stepResponse.Success)
            {
                return new StepExecutionResult(false, stepRequest.Attempts,
                    $"Step {step.Step_Kind} failed: {stepResponse.Failure_Details}");
            }
        }

        return new StepExecutionResult(true, 0, null);
    }

    /// <summary>
    /// EVENT_PRIMED sentinel → push 50 years (schedule only fires via event-close SP).
    /// Real cron → compute next UTC occurrence using Cronos.
    /// </summary>
    private static DateTime CalculateNextRunAt(string cronExpression, string? timezone)
    {
        if (string.IsNullOrWhiteSpace(cronExpression) || cronExpression == EventPrimedToken)
            return DateTime.UtcNow.AddYears(50);

        try
        {
            var expression = CronExpression.Parse(cronExpression);
            var tz         = TimeZoneInfo.FindSystemTimeZoneById(timezone ?? "UTC");
            var next       = expression.GetNextOccurrence(DateTimeOffset.UtcNow, tz);
            return next?.UtcDateTime ?? DateTime.UtcNow.AddHours(1);
        }
        catch
        {
            return DateTime.UtcNow.AddHours(1);
        }
    }

    private record StepExecutionResult(bool Succeeded, short RetryCount, string? FailureReason);
}
