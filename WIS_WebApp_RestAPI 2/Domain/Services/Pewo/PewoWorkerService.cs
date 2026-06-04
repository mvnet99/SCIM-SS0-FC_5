using Domain.ApiModels;
using Domain.ApiModels.Pewo;
using Domain.Services.Interfaces;
using Domain.Services.Interfaces.Pewo;
using Domain.Services.Pewo.Steps;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace Domain.Services.Pewo
{
    /// <summary>
    /// Orchestrates the PEWO job loop.
    ///
    /// Called by POST /api/pewo/worker/run (triggered by AKS CronJob).
    ///
    /// TOTALS_CHECK (step 1) uses the existing ITotalsValidationService.ValidateNgen()
    /// — the same service that powers TotalsValidationController.GetNgenTotalsCheck().
    /// No new totals service is needed.
    ///
    /// Steps 2–6 run purely in-process (no API calls).
    /// </summary>
    public class PewoWorkerService : IPewoWorkerService
    {
        private readonly IPewoJobDataService _data;
        private readonly ITotalsValidationService _totalsValidationService;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly ReadBlobStep _readBlobStep;
        private readonly ZipStep _zipStep;
        private readonly SftpStep _sftpStep;
        private readonly ArchiveStep _archiveStep;
        private readonly EmailStep _emailStep;
        private readonly ILogger<PewoWorkerService> _logger;

        private static readonly string WorkerId =
            $"pewo-{Environment.MachineName}-{Guid.NewGuid():N}"[..Math.Min(50, $"pewo-{Environment.MachineName}-{Guid.NewGuid():N}".Length)];

        private static readonly bool DryRun =
            string.Equals(Environment.GetEnvironmentVariable("PEWO_DRY_RUN"), "true", StringComparison.OrdinalIgnoreCase);

        public PewoWorkerService(
            IPewoJobDataService data,
            ITotalsValidationService totalsValidationService,
            IHttpContextAccessor httpContextAccessor,
            ReadBlobStep readBlobStep,
            ZipStep zipStep,
            SftpStep sftpStep,
            ArchiveStep archiveStep,
            EmailStep emailStep,
            ILogger<PewoWorkerService> logger)
        {
            _data                    = data;
            _totalsValidationService = totalsValidationService;
            _httpContextAccessor     = httpContextAccessor;
            _readBlobStep            = readBlobStep;
            _zipStep                 = zipStep;
            _sftpStep                = sftpStep;
            _archiveStep             = archiveStep;
            _emailStep               = emailStep;
            _logger                  = logger;
        }

        public async Task<WorkerRunResponse> RunAsync(CancellationToken cancellationToken)
        {
            var sw = Stopwatch.StartNew();
            _logger.LogInformation("[PEWO] Worker.RunAsync started. WorkerId={WorkerId} DryRun={DryRun}", WorkerId, DryRun);

            var dueJobs = await _data.GetDueJobsAsync(cancellationToken);
            _logger.LogInformation("[PEWO] Due jobs: {Count}", dueJobs.Count);

            int succeeded = 0, failed = 0;

            foreach (var job in dueJobs)
            {
                var runId = job.RunId ?? Guid.NewGuid();
                try
                {
                    var acquired = await _data.AcquireScheduleLockAsync(job.ScheduleId, WorkerId, cancellationToken);
                    if (!acquired)
                    {
                        _logger.LogWarning("[PEWO] Lock not acquired for ScheduleId={Id} — skipping", job.ScheduleId);
                        continue;
                    }

                    if (job.JobSource == "SCHEDULE")
                    {
                        runId = Guid.NewGuid();
                        await _data.CreateWorkflowRunAsync(runId, job.WorkflowTypeId, job.ScheduleId, job.MaxRetries, cancellationToken);
                    }

                    _logger.LogInformation("[PEWO] RunId={RunId} Workflow={Code} Source={Source}", runId, job.WorkflowCode, job.JobSource);

                    var runResult = await ExecuteStepsAsync(job, runId, cancellationToken);

                    DateTime? retryAt  = null;
                    int       newRetry = runResult.RetryCount;
                    if (!runResult.Succeeded && newRetry < job.MaxRetries)
                        retryAt = DateTime.UtcNow.AddSeconds((int)Math.Pow(2, newRetry) * 60);

                    await _data.SetRunTerminalStatusAsync(
                        runId,
                        runResult.Succeeded ? "COMPLETED" : "FAILED",
                        runResult.FailReason,
                        retryAt,
                        newRetry,
                        cancellationToken);

                    var nextRunAt = CalculateNextRunAt(job.CronExpression);
                    await _data.ReleaseScheduleLockAsync(
                        job.ScheduleId, WorkerId, nextRunAt, runId,
                        runResult.Succeeded ? "COMPLETED" : "FAILED",
                        cancellationToken);

                    if (runResult.Succeeded) succeeded++; else failed++;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[PEWO] Unhandled error ScheduleId={Id} RunId={RunId}", job.ScheduleId, runId);
                    failed++;
                    try
                    {
                        await _data.SetRunTerminalStatusAsync(runId, "FAILED", ex.Message, DateTime.UtcNow.AddMinutes(5), 0, cancellationToken);
                        await _data.ReleaseScheduleLockAsync(job.ScheduleId, WorkerId, CalculateNextRunAt(job.CronExpression), runId, "FAILED", cancellationToken);
                    }
                    catch (Exception inner) { _logger.LogError(inner, "[PEWO] Failed to release lock for ScheduleId={Id}", job.ScheduleId); }
                }
            }

            sw.Stop();
            _logger.LogInformation("[PEWO] Worker complete. Succeeded={S} Failed={F} Duration={D}ms", succeeded, failed, sw.ElapsedMilliseconds);

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

            string? failReason = null;
            int     retryCount = 0;

            foreach (var step in steps.OrderBy(s => s.StepOrder))
            {
                // Resume: SUCCESS steps are skipped; their artifact is restored for downstream steps
                if (step.Status == "SUCCESS")
                {
                    _logger.LogInformation("[PEWO] Step {Kind} order={Order} already SUCCESS — skipping", step.StepKind, step.StepOrder);
                    RestoreStepContext(stepCtx, step);
                    continue;
                }

                var attempts  = step.Attempts + 1;
                var startedAt = DateTime.UtcNow;

                _logger.LogInformation("[PEWO] Executing step {Kind} order={Order} attempt={A}", step.StepKind, step.StepOrder, attempts);

                StepResult result;
                try
                {
                    result = await DispatchStepAsync(step, stepCtx, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[PEWO] Step {Kind} threw on attempt {A}", step.StepKind, attempts);
                    result = StepResult.Fail($"Exception: {ex.Message}");
                }

                await _data.UpsertStepRunAsync(
                    runId, step.StepDefId,
                    result.Success ? "SUCCESS" : "FAILED",
                    attempts, result.Reason, result.ArtifactRef,
                    startedAt, DateTime.UtcNow,
                    cancellationToken);

                if (!result.Success)
                {
                    retryCount = attempts;
                    failReason = $"Step {step.StepKind} (order={step.StepOrder}) failed: {result.Reason}";
                    _logger.LogWarning("[PEWO] Step {Kind} FAILED (attempt {A}/{Max}): {Reason}", step.StepKind, attempts, step.MaxAttempts, result.Reason);
                    break; // Stop on first failure — retry_at drives next pickup
                }

                _logger.LogInformation("[PEWO] Step {Kind} SUCCESS. ArtifactRef={Ref}", step.StepKind, result.ArtifactRef);
            }

            return new RunResult(string.IsNullOrEmpty(failReason), failReason, retryCount);
        }

        private async Task<StepResult> DispatchStepAsync(RunResumeStepDto step, PewoStepContext ctx, CancellationToken cancellationToken)
        {
            // Build a per-step context with the step's own config JSON injected
            var stepCtx = new PewoStepContext
            {
                RunId           = ctx.RunId,
                WorkflowCode    = ctx.WorkflowCode,
                DryRun          = ctx.DryRun,
                ConfigJson      = step.Config,
                StagingBlobPath = ctx.StagingBlobPath
            };
            foreach (var (k, v) in ctx.InMemoryFiles)
                stepCtx.InMemoryFiles[k] = v;

            switch (step.StepKind)
            {
                case "TOTALS_CHECK":
                    // ── Step 1: delegate to the existing ValidateNgen service ──────────────
                    // ValidateNgen needs an eventGuid. For PEWO, the event context comes from
                    // JobEvent.event_id. The eventGuid is resolved from the config JSON
                    // or passed as a parameter via JobEvent metadata.
                    // For now we derive it from the step config (set by seed data / JobEvent).
                    var tcCfg     = System.Text.Json.JsonSerializer.Deserialize<TotalsCheckStepConfig>(step.Config ?? "{}") ?? new();
                    var tcResult  = _totalsValidationService.ValidateNgen(tcCfg.EventGuid);

                    if (!tcResult.Success)
                    {
                        var errors = tcResult.Messages
                            .Where(m => m.Status == "Error")
                            .Select(m => m.Step);
                        return StepResult.Fail(string.Join("; ", errors));
                    }

                    var summary = $"checked:{tcResult.Messages.Count(m => m.Status == "Info")} qty={tcResult.TotalQty} ext={tcResult.TotalExt}";
                    return StepResult.Ok(summary);

                case "READ_BLOB":
                    var rbResult = await _readBlobStep.ExecuteAsync(stepCtx, cancellationToken);
                    if (rbResult.Success)
                        foreach (var (k, v) in stepCtx.InMemoryFiles) ctx.InMemoryFiles[k] = v;
                    return rbResult;

                case "ZIP":
                    var zipResult = await _zipStep.ExecuteAsync(stepCtx, cancellationToken);
                    if (zipResult.Success) ctx.StagingBlobPath = stepCtx.StagingBlobPath;
                    return zipResult;

                case "SFTP":
                    stepCtx.StagingBlobPath = ctx.StagingBlobPath;
                    return await _sftpStep.ExecuteAsync(stepCtx, cancellationToken);

                case "ARCHIVE":
                    return await _archiveStep.ExecuteAsync(stepCtx, cancellationToken);

                case "EMAIL":
                    return await _emailStep.ExecuteAsync(stepCtx, step.ArtifactRef, cancellationToken);

                default:
                    return StepResult.Fail($"Unknown step kind: {step.StepKind}");
            }
        }

        private static void RestoreStepContext(PewoStepContext ctx, RunResumeStepDto step)
        {
            if (step.StepKind == "ZIP" && !string.IsNullOrEmpty(step.ArtifactRef))
                ctx.StagingBlobPath = step.ArtifactRef;
        }

        private static DateTime CalculateNextRunAt(string cronExpression)
        {
            try
            {
                var schedule = Cronos.CronExpression.Parse(cronExpression);
                return schedule.GetNextOccurrence(DateTime.UtcNow, TimeZoneInfo.Utc) ?? DateTime.UtcNow.AddHours(1);
            }
            catch { return DateTime.UtcNow.AddHours(1); }
        }

        private record RunResult(bool Succeeded, string? FailReason, int RetryCount);

        // Config baked into WorkflowStepDef.config for the TOTALS_CHECK step.
        // The EventGuid comes from JobEvent.metadata_json or is set at run creation time.
        private record TotalsCheckStepConfig
        {
            public string EventGuid { get; init; } = string.Empty;
        }
    }
}
