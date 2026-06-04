using Domain.ApiModels.Pewo;
using Domain.Services.Interfaces.Pewo;
using Domain.Services.Pewo.Steps;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace Domain.Services.Pewo
{
    /// <summary>
    /// Orchestrates the PEWO job loop.
    ///
    /// Called by POST /api/pewo/worker/run (triggered by AKS CronJob).
    /// 1. Fetch due jobs from Database API.
    /// 2. For each job: acquire lock, create run, execute steps in order, release lock.
    /// 3. Retry logic: auto-retry set via WorkflowRun.retry_at; manual retry via API.
    ///
    /// Only TOTALS_CHECK calls the API. Steps 2–6 run purely in-process.
    /// </summary>
    public class PewoWorkerService : IPewoWorkerService
    {
        private readonly IPewoJobDataService _data;
        private readonly IPewoTotalsCheckService _totalsCheck;
        private readonly ReadBlobStep _readBlobStep;
        private readonly ZipStep _zipStep;
        private readonly SftpStep _sftpStep;
        private readonly ArchiveStep _archiveStep;
        private readonly EmailStep _emailStep;
        private readonly ILogger<PewoWorkerService> _logger;

        // Worker instance ID — used as the lock owner in Schedule.locked_by
        private static readonly string WorkerId = $"worker-{Environment.MachineName}-{Guid.NewGuid():N}".Substring(0, 50);

        private const bool DryRun = false; // TODO: wire to env var / config for safe testing

        public PewoWorkerService(
            IPewoJobDataService data,
            IPewoTotalsCheckService totalsCheck,
            ReadBlobStep readBlobStep,
            ZipStep zipStep,
            SftpStep sftpStep,
            ArchiveStep archiveStep,
            EmailStep emailStep,
            ILogger<PewoWorkerService> logger)
        {
            _data        = data;
            _totalsCheck = totalsCheck;
            _readBlobStep = readBlobStep;
            _zipStep      = zipStep;
            _sftpStep     = sftpStep;
            _archiveStep  = archiveStep;
            _emailStep    = emailStep;
            _logger       = logger;
        }

        public async Task<WorkerRunResponse> RunAsync(CancellationToken cancellationToken)
        {
            var sw = Stopwatch.StartNew();
            _logger.LogInformation("[PEWO] Worker.RunAsync started. WorkerId={WorkerId}", WorkerId);

            var dueJobs = await _data.GetDueJobsAsync(cancellationToken);
            _logger.LogInformation("[PEWO] Due jobs: {Count}", dueJobs.Count);

            int succeeded = 0, failed = 0;

            foreach (var job in dueJobs)
            {
                var runId = job.RunId ?? Guid.NewGuid();

                try
                {
                    // ── Acquire run-lock ──────────────────────────────────────
                    var acquired = await _data.AcquireScheduleLockAsync(job.ScheduleId, WorkerId, cancellationToken);
                    if (!acquired)
                    {
                        _logger.LogWarning("[PEWO] Lock not acquired for ScheduleId={Id} — another worker is running it", job.ScheduleId);
                        continue;
                    }

                    // ── Create WorkflowRun (new fire) or resume existing retry ─
                    if (job.JobSource == "SCHEDULE")
                    {
                        runId = Guid.NewGuid();
                        await _data.CreateWorkflowRunAsync(runId, job.WorkflowTypeId, job.ScheduleId, job.MaxRetries, cancellationToken);
                    }
                    // For RETRY source, runId is already set from job.RunId

                    _logger.LogInformation("[PEWO] Processing RunId={RunId} Workflow={Code} Source={Source}",
                        runId, job.WorkflowCode, job.JobSource);

                    // ── Execute steps ─────────────────────────────────────────
                    var runResult = await ExecuteStepsAsync(job, runId, cancellationToken);

                    // ── Set terminal status ───────────────────────────────────
                    DateTime? retryAt   = null;
                    int       newRetry  = runResult.RetryCount;
                    if (!runResult.Succeeded && newRetry < job.MaxRetries)
                    {
                        // Exponential backoff: 2^retry * 60 seconds
                        var backoffSec = (int)Math.Pow(2, newRetry) * 60;
                        retryAt = DateTime.UtcNow.AddSeconds(backoffSec);
                    }

                    await _data.SetRunTerminalStatusAsync(
                        runId,
                        runResult.Succeeded ? "COMPLETED" : "FAILED",
                        runResult.FailReason,
                        retryAt,
                        newRetry,
                        cancellationToken);

                    // ── Release lock and advance schedule ─────────────────────
                    var nextRunAt = CalculateNextRunAt(job.CronExpression);
                    await _data.ReleaseScheduleLockAsync(
                        job.ScheduleId,
                        WorkerId,
                        nextRunAt,
                        runId,
                        runResult.Succeeded ? "COMPLETED" : "FAILED",
                        cancellationToken);

                    if (runResult.Succeeded)
                        succeeded++;
                    else
                        failed++;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[PEWO] Unhandled error for ScheduleId={Id} RunId={RunId}", job.ScheduleId, runId);
                    failed++;
                    try
                    {
                        await _data.SetRunTerminalStatusAsync(runId, "FAILED", ex.Message, DateTime.UtcNow.AddMinutes(5), 0, cancellationToken);
                        var nextRunAt = CalculateNextRunAt(job.CronExpression);
                        await _data.ReleaseScheduleLockAsync(job.ScheduleId, WorkerId, nextRunAt, runId, "FAILED", cancellationToken);
                    }
                    catch (Exception inner)
                    {
                        _logger.LogError(inner, "[PEWO] Failed to release lock for ScheduleId={Id}", job.ScheduleId);
                    }
                }
            }

            sw.Stop();
            _logger.LogInformation("[PEWO] Worker.RunAsync complete. Succeeded={S} Failed={F} Duration={D}ms",
                succeeded, failed, sw.ElapsedMilliseconds);

            return new WorkerRunResponse
            {
                JobsProcessed = dueJobs.Count,
                JobsSucceeded = succeeded,
                JobsFailed    = failed,
                DurationMs    = sw.ElapsedMilliseconds.ToString()
            };
        }

        // ─────────────────────────────────────────────────────────────────────
        // Step execution loop with resume logic
        // ─────────────────────────────────────────────────────────────────────

        private async Task<RunResult> ExecuteStepsAsync(DueJobDto job, Guid runId, CancellationToken cancellationToken)
        {
            var steps = await _data.GetRunResumeAsync(runId, job.WorkflowTypeId, cancellationToken);

            var stepCtx = new PewoStepContext
            {
                RunId        = runId,
                WorkflowCode = job.WorkflowCode,
                DryRun       = DryRun
            };

            var retryCount  = 0;
            string? failReason = null;

            foreach (var step in steps.OrderBy(s => s.StepOrder))
            {
                // Resume logic
                if (step.Status == "SUCCESS")
                {
                    _logger.LogInformation("[PEWO] Step {Kind} (order={Order}) already SUCCESS — skipping", step.StepKind, step.StepOrder);
                    // Restore context from completed steps
                    RestoreStepContext(stepCtx, step);
                    continue;
                }

                var attempts   = step.Attempts + 1;
                var startedAt  = DateTime.UtcNow;

                _logger.LogInformation("[PEWO] Executing step {Kind} (order={Order}) attempt={Attempt}",
                    step.StepKind, step.StepOrder, attempts);

                StepResult result;
                try
                {
                    result = await DispatchStepAsync(step, stepCtx, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[PEWO] Step {Kind} threw exception on attempt {A}", step.StepKind, attempts);
                    result = StepResult.Fail($"Exception: {ex.Message}");
                }

                var finishedAt = DateTime.UtcNow;
                var status     = result.Success ? "SUCCESS" : "FAILED";

                await _data.UpsertStepRunAsync(
                    runId,
                    step.StepDefId,
                    status,
                    attempts,
                    result.Reason,
                    result.ArtifactRef,
                    startedAt,
                    finishedAt,
                    cancellationToken);

                if (!result.Success)
                {
                    retryCount  = attempts;
                    failReason  = $"Step {step.StepKind} (order={step.StepOrder}) failed: {result.Reason}";
                    _logger.LogWarning("[PEWO] Step {Kind} FAILED (attempt {A}/{Max}): {Reason}",
                        step.StepKind, attempts, step.MaxAttempts, result.Reason);

                    if (attempts >= step.MaxAttempts)
                    {
                        _logger.LogWarning("[PEWO] Step {Kind} exceeded max attempts — aborting run", step.StepKind);
                        break;
                    }

                    // Await step-level backoff before next cron wake (worker exits; retry_at drives next pickup)
                    break;
                }

                _logger.LogInformation("[PEWO] Step {Kind} SUCCESS. ArtifactRef={Ref}", step.StepKind, result.ArtifactRef);
            }

            var succeeded = string.IsNullOrEmpty(failReason);
            return new RunResult(succeeded, failReason, retryCount);
        }

        private async Task<StepResult> DispatchStepAsync(RunResumeStepDto step, PewoStepContext ctx, CancellationToken cancellationToken)
        {
            // Inject step config into context
            ctx = ctx with { };
            var ctxWithConfig = new PewoStepContext
            {
                RunId        = ctx.RunId,
                WorkflowCode = ctx.WorkflowCode,
                DryRun       = ctx.DryRun,
                ConfigJson   = step.Config,
                StagingBlobPath = ctx.StagingBlobPath
            };
            // Copy in-memory files from outer context
            foreach (var (k, v) in ctx.InMemoryFiles)
                ctxWithConfig.InMemoryFiles[k] = v;

            switch (step.StepKind)
            {
                case "TOTALS_CHECK":
                    // Step 1 — only step that calls the internal API
                    // Config provides container + file patterns
                    var tcCfg = System.Text.Json.JsonSerializer.Deserialize<TotalsCheckStepConfig>(step.Config ?? "{}") ?? new();
                    var tcReq = new TotalsCheckRequest
                    {
                        RunId          = ctx.RunId,
                        SourceContainer = tcCfg.SourceContainer,
                        FilePatternGm  = tcCfg.FilePatternGm,
                        FilePatternPrpc = tcCfg.FilePatternPrpc,
                        StoreNo        = string.Empty
                    };
                    var tcResp = await _totalsCheck.CheckAsync(tcReq, cancellationToken);
                    return tcResp.Passed
                        ? StepResult.Ok($"checked:{tcResp.FilesChecked}")
                        : StepResult.Fail(tcResp.Reason);

                case "READ_BLOB":
                    return await _readBlobStep.ExecuteAsync(ctxWithConfig, cancellationToken);

                case "ZIP":
                    var zipResult = await _zipStep.ExecuteAsync(ctxWithConfig, cancellationToken);
                    if (zipResult.Success) ctx.StagingBlobPath = ctxWithConfig.StagingBlobPath;
                    return zipResult;

                case "SFTP":
                    ctxWithConfig.StagingBlobPath = ctx.StagingBlobPath;
                    return await _sftpStep.ExecuteAsync(ctxWithConfig, cancellationToken);

                case "ARCHIVE":
                    return await _archiveStep.ExecuteAsync(ctxWithConfig, cancellationToken);

                case "EMAIL":
                    return await _emailStep.ExecuteAsync(ctxWithConfig, step.ArtifactRef, cancellationToken);

                default:
                    return StepResult.Fail($"Unknown step kind: {step.StepKind}");
            }
        }

        private static void RestoreStepContext(PewoStepContext ctx, RunResumeStepDto step)
        {
            // Restore ZIP's staging path so SFTP can find it on resume
            if (step.StepKind == "ZIP" && !string.IsNullOrEmpty(step.ArtifactRef))
                ctx.StagingBlobPath = step.ArtifactRef;
        }

        /// <summary>
        /// Calculates the next UTC fire time from a cron expression.
        /// Uses Cronos (add package Cronos to .csproj).
        /// </summary>
        private static DateTime CalculateNextRunAt(string cronExpression)
        {
            try
            {
                var schedule = Cronos.CronExpression.Parse(cronExpression);
                return schedule.GetNextOccurrence(DateTime.UtcNow, TimeZoneInfo.Utc)
                    ?? DateTime.UtcNow.AddHours(1);
            }
            catch
            {
                // Fallback: 1 hour from now
                return DateTime.UtcNow.AddHours(1);
            }
        }

        private record RunResult(bool Succeeded, string? FailReason, int RetryCount);

        private record TotalsCheckStepConfig
        {
            public string SourceContainer  { get; init; } = "fc-hold-target";
            public string FilePatternGm    { get; init; } = "TAR_ITM_GM_";
            public string FilePatternPrpc  { get; init; } = "TAR_ITM_PRPC_";
        }
    }
}
