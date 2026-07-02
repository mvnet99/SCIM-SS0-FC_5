using Cronos;
using Domain.ApiModels.Pewo;
using Domain.Services.Interfaces.Pewo;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WS.FC.Dapper.Shared.DTOs.Pewo;

namespace Domain.Services.Pewo;

/// <summary>
/// PEWO job loop. Entry point: POST /api/Pewo/worker/run.
///
/// Fixes applied:
///   Fix 9  — AdvanceSchedule skipped for failed ON_EVENT_CLOSE runs.
///             ON_EVENT_CLOSE schedule is shared across all events. Advancing it
///             on failure would overwrite a legitimate future event's Next_Run_At.
///             Cron-based schedules (GM_PRC_DELIVERY) always advance regardless of result.
///   Fix 15 — GetDueJobsAsync wrapped in its own try/catch. SP failure no longer
///             kills the entire worker tick — returns empty response and logs the error.
///   Fix 16 — Artifact_Ref truncated to 490 chars before UpsertStepRunAsync.
///             Pewo_WorkflowStepRun.Artifact_Ref is NVARCHAR(500) — truncation
///             with warning log prevents silent SQL truncation.
///   Fix 18 — Dead letter email via IPewoStepService.EmailAsync when run hits
///             max retries permanently. Reads EMAIL step config from loaded steps
///             to get recipients — no extra DB call needed.
/// </summary>
public class PewoWorkerService : IPewoWorkerService
{
    private const string EventPrimedToken  = "ON_EVENT_CLOSE";
    private const int    MaxArtifactRefLen = 490; // Fix 16: NVARCHAR(500) column — 10 char buffer

    private readonly IPewoJobDataService        _jobDataService;
    private readonly IPewoLogService            _logService;
    private readonly IPewoStepService           _stepService;
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

        // Fix 15: GetDueJobsAsync wrapped in own try/catch.
        // SP failure (deadlock, timeout) no longer crashes entire tick.
        List<DueJobDto> dueJobs;
        try
        {
            dueJobs = await _jobDataService.GetDueJobsAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[PEWO] GetDueJobsAsync failed — skipping this tick. Will retry next interval.");
            return new PewoWorkerRunResponse
            {
                JobsProcessed = 0, JobsSucceeded = 0, JobsFailed = 0,
                DurationMs = sw.ElapsedMilliseconds.ToString()
            };
        }

        _logger.LogInformation("[PEWO] Due jobs: {Count}", dueJobs.Count);

        int succeeded = 0, failed = 0;

        foreach (var job in dueJobs)
        {
            int runId = 0;
            try
            {
                // SCHEDULE / SAFETY_NET → create new run
                // RETRY / NEW_CHILD / PENDING_EVENT → reuse existing id_WorkflowRun
                runId = job.Job_Source == "SCHEDULE" || job.Job_Source == "SAFETY_NET"
                    ? await _jobDataService.CreateWorkflowRunAsync(
                        job.Id_Schedule, job.Id_CustomerWorkflowType, job.Max_Retries, cancellationToken)
                    : job.Id_WorkflowRun!.Value;

                _logger.LogInformation("[PEWO] Processing RunId={RunId} Workflow={Code} Source={Source}",
                    runId, job.WorkflowType_Code, job.Job_Source);

                await _logService.LogAsync(runId, null, null, null, "INFO",
                    $"Job picked up. WorkflowCode={job.WorkflowType_Code} Source={job.Job_Source}",
                    null, cancellationToken);

                var runResult = await ExecuteStepsAsync(job, runId, cancellationToken);

                DateTime? retryAt    = null;
                short     retryCount = runResult.RetryCount;

                if (!runResult.Succeeded && retryCount < job.Max_Retries)
                    retryAt = DateTime.UtcNow.AddMinutes((int)Math.Pow(2, retryCount));

                await _jobDataService.SetRunTerminalStatusAsync(
                    runId,
                    runResult.Succeeded ? "COMPLETED" : "FAILED",
                    runResult.FailureReason,
                    retryAt,
                    retryCount,
                    cancellationToken);

                // Fix 9: Skip AdvanceSchedule for failed ON_EVENT_CLOSE runs.
                // ON_EVENT_CLOSE schedule is shared across all stores — advancing on failure
                // could overwrite a legitimate Next_Run_At set by another event's close.
                // Cron-based schedules always advance regardless of result.
                var isEventDriven = string.Equals(job.Cron_Expression, EventPrimedToken,
                    StringComparison.OrdinalIgnoreCase);

                if (!isEventDriven || runResult.Succeeded)
                {
                    var nextRunAt = CalculateNextRunAt(job.Cron_Expression, job.Timezone);
                    await _jobDataService.AdvanceScheduleAsync(
                        job.Id_Schedule, nextRunAt, runId,
                        runResult.Succeeded ? "COMPLETED" : "FAILED",
                        cancellationToken);
                }

                // Fix 18: Dead letter email when run permanently fails (max retries exhausted).
                // Reuses IPewoStepService.EmailAsync with EMAIL step config from the run's steps.
                // Same recipients and SendGrid path as normal completion email.
                if (!runResult.Succeeded && retryCount >= job.Max_Retries
                    && !string.IsNullOrEmpty(runResult.EmailStepConfig))
                {
                    await SendDeadLetterEmailAsync(
                        runId, job.WorkflowType_Code, runResult, cancellationToken);
                }

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

        var firstEvent = events.FirstOrDefault();
        var eventGuid  = events.FirstOrDefault(e => e.Event_Guid.HasValue)?.Event_Guid?.ToString();
        var storeNo    = events.FirstOrDefault(e => !string.IsNullOrEmpty(e.Store_No))?.Store_No;
        var eventDate  = events.FirstOrDefault(e => e.Event_Date.HasValue)?.Event_Date;

        // Fix 18: capture EMAIL step config for dead letter if run fails permanently
        var emailStepConfig = steps.FirstOrDefault(s =>
            string.Equals(s.Step_Kind, "EMAIL", StringComparison.OrdinalIgnoreCase))?.Config;

        foreach (var step in steps.OrderBy(s => s.Step_Order))
        {
            if (string.Equals(step.Status, "COMPLETED", StringComparison.OrdinalIgnoreCase))
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

            // Fix 16: Artifact_Ref length guard — NVARCHAR(500) column, truncate with warning
            var artifactRef = stepResponse.Artifact_Ref;
            if (artifactRef?.Length > MaxArtifactRefLen)
            {
                _logger.LogWarning(
                    "[PEWO] RunId={RunId} Step={Kind} Artifact_Ref truncated from {Len} to {Max} chars. " +
                    "Consider increasing Pewo_WorkflowStepRun.Artifact_Ref column size.",
                    runId, step.Step_Kind, artifactRef.Length, MaxArtifactRefLen);
                artifactRef = artifactRef[..MaxArtifactRefLen];
            }

            await _jobDataService.UpsertStepRunAsync(
                runId, step.Id_WorkflowStepDef, step.Step_Kind,
                newStatus, stepRequest.Attempts,
                artifactRef, stepResponse.Failure_Details,
                startTime, endTime, cancellationToken);

            await _logService.LogAsync(runId, null, storeNo, step.Step_Kind,
                stepResponse.Success ? "INFO" : "ERROR",
                stepResponse.Success
                    ? $"Step {step.Step_Kind} COMPLETED"
                    : $"Step {step.Step_Kind} FAILED: {stepResponse.Failure_Details}",
                eventGuid, cancellationToken);

            if (!stepResponse.Success)
            {
                return new StepExecutionResult(
                    false, stepRequest.Attempts,
                    $"Step {step.Step_Kind} failed: {stepResponse.Failure_Details}",
                    emailStepConfig);
            }
        }

        return new StepExecutionResult(true, 0, null, emailStepConfig);
    }

    /// <summary>
    /// Fix 18: Dead letter email sent when a run permanently fails (max retries exhausted).
    /// Reuses IPewoStepService.EmailAsync with EMAIL step config from the run's own step definitions.
    /// Overrides subject to clearly indicate failure. Never blocks the worker — exceptions swallowed.
    /// </summary>
    private async Task SendDeadLetterEmailAsync(
        int runId, string workflowTypeCode, StepExecutionResult runResult, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogWarning(
                "[PEWO] RunId={RunId} permanently FAILED after max retries — sending dead letter email",
                runId);

            var deadLetterConfig = InjectDeadLetterSubject(
                runResult.EmailStepConfig, workflowTypeCode, runId);

            var deadLetterRequest = new PewoStepRequest
            {
                Id_WorkflowRun     = runId,
                Id_WorkflowStepDef = 0,
                Step_Kind          = "EMAIL",
                Config             = deadLetterConfig,
                Attempts           = 1,
                Max_Attempts       = 1
            };

            await _stepService.EmailAsync(deadLetterRequest, cancellationToken);

            _logger.LogInformation("[PEWO] RunId={RunId} dead letter email sent", runId);
        }
        catch (Exception ex)
        {
            // Dead letter email failure must never crash the worker
            _logger.LogError(ex, "[PEWO] RunId={RunId} dead letter email failed — non-critical", runId);
        }
    }

    /// <summary>Injects a failure subject into the EMAIL step Config JSON for dead letter emails.</summary>
    private static string? InjectDeadLetterSubject(string? config, string workflowTypeCode, int runId)
    {
        if (string.IsNullOrWhiteSpace(config)) return config;
        try
        {
            using var doc  = System.Text.Json.JsonDocument.Parse(config);
            var dict       = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var prop in doc.RootElement.EnumerateObject())
                dict[prop.Name] = prop.Value.GetString() ?? string.Empty;
            dict["subject"] = $"PEWO FAILED — {workflowTypeCode} RunId={runId} — Max retries exceeded. Manual intervention required.";
            return System.Text.Json.JsonSerializer.Serialize(dict);
        }
        catch { return config; }
    }

    /// <summary>
    /// ON_EVENT_CLOSE sentinel → push 50 years (schedule fires only via event-close SP).
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

    private record StepExecutionResult(
        bool    Succeeded,
        short   RetryCount,
        string? FailureReason,
        string? EmailStepConfig);
}
