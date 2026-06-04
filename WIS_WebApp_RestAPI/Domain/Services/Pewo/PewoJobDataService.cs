using Domain.Services.Interfaces.Pewo;
using Microsoft.Extensions.Logging;
using WS.FC.DatabaseService.Wrapper.Interfaces;

namespace Domain.Services.Pewo
{
    /// <summary>
    /// Thin adapter: translates IPewoJobDataService calls into IPewoDataServiceClient calls.
    /// The HttpClient base URL is set to PewoDataServiceUrl from Key Vault via ServicesConfiguration.
    /// </summary>
    public class PewoJobDataService : IPewoJobDataService
    {
        private readonly IPewoDataServiceClient _client;
        private readonly ILogger<PewoJobDataService> _logger;

        public PewoJobDataService(IPewoDataServiceClient client, ILogger<PewoJobDataService> logger)
        {
            _client = client;
            _logger = logger;
        }

        public async Task<List<DueJobDto>> GetDueJobsAsync(CancellationToken cancellationToken)
        {
            var dtos = await _client.GetDueJobsAsync(cancellationToken);
            return dtos.Select(d => new DueJobDto
            {
                ScheduleId     = d.ScheduleId,
                WorkflowTypeId = d.WorkflowTypeId,
                ScheduleName   = d.ScheduleName,
                CronExpression = d.CronExpression,
                Timezone       = d.Timezone,
                NextRunAt      = d.NextRunAt,
                Status         = d.Status,
                LastRunId      = d.LastRunId,
                WorkflowCode   = d.WorkflowCode,
                WorkflowName   = d.WorkflowName,
                MaxRetries     = d.MaxRetries,
                RunId          = d.RunId,
                JobSource      = d.JobSource
            }).ToList();
        }

        public async Task<List<RunResumeStepDto>> GetRunResumeAsync(Guid runId, int workflowTypeId, CancellationToken cancellationToken)
        {
            var dtos = await _client.GetRunResumeAsync(runId, workflowTypeId, cancellationToken);
            return dtos.Select(d => new RunResumeStepDto
            {
                StepDefId      = d.StepDefId,
                StepOrder      = d.StepOrder,
                StepKind       = d.StepKind,
                StepName       = d.StepName,
                Config         = d.Config,
                MaxAttempts    = d.MaxAttempts,
                BackoffSeconds = d.BackoffSeconds,
                StepRunId      = d.StepRunId,
                Status         = d.Status,
                Attempts       = d.Attempts,
                Reason         = d.Reason,
                ArtifactRef    = d.ArtifactRef
            }).ToList();
        }

        public Task<bool> AcquireScheduleLockAsync(int scheduleId, string workerId, CancellationToken cancellationToken)
            => _client.AcquireScheduleLockAsync(scheduleId, workerId, cancellationToken);

        public Task ReleaseScheduleLockAsync(int scheduleId, string workerId, DateTime nextRunAt, Guid lastRunId, string lastStatus, CancellationToken cancellationToken)
            => _client.ReleaseScheduleLockAsync(scheduleId, workerId, nextRunAt, lastRunId, lastStatus, cancellationToken);

        public Task CreateWorkflowRunAsync(Guid runId, int workflowTypeId, int scheduleId, int maxRetries, CancellationToken cancellationToken)
            => _client.CreateWorkflowRunAsync(runId, workflowTypeId, scheduleId, maxRetries, cancellationToken);

        public Task UpsertStepRunAsync(Guid runId, int stepDefId, string status, int attempts, string? reason, string? artifactRef, DateTime? startedAt, DateTime? finishedAt, CancellationToken cancellationToken)
            => _client.UpsertStepRunAsync(runId, stepDefId, status, attempts, reason, artifactRef, startedAt, finishedAt, cancellationToken);

        public Task SetRunTerminalStatusAsync(Guid runId, string status, string? reason, DateTime? retryAt, int retryCount, CancellationToken cancellationToken)
            => _client.SetRunTerminalStatusAsync(runId, status, reason, retryAt, retryCount, cancellationToken);

        public Task ResetRunForRetryAsync(Guid runId, CancellationToken cancellationToken)
            => _client.ResetRunForRetryAsync(runId, cancellationToken);
    }
}
