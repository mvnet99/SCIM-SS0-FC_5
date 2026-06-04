using WS.FC.DatabaseService.Wrapper.Interfaces;
using WS.FC.DatabaseService.Wrapper.ServiceClients.Base;
using WS.FC.Dapper.Shared.DTOs.Pewo;

namespace WS.FC.DatabaseService.Wrapper.ServiceClients
{
    public class PewoDataServiceClient : BaseServiceClient, IPewoDataServiceClient
    {
        public PewoDataServiceClient(HttpClient httpClient) : base(httpClient) { }

        public Task<List<DueJobDto>> GetDueJobsAsync(CancellationToken cancellationToken)
            => GetListAsync<DueJobDto>("due-jobs", cancellationToken);

        public Task<List<RunResumeStepDto>> GetRunResumeAsync(Guid runId, int workflowTypeId, CancellationToken cancellationToken)
            => GetListAsync<RunResumeStepDto>($"runs/{runId}/resume?workflowTypeId={workflowTypeId}", cancellationToken);

        public async Task<bool> AcquireScheduleLockAsync(int scheduleId, string workerId, CancellationToken cancellationToken)
        {
            var result = await PostAsJsonAndDeserializeItemAsync<LockResultDto, AcquireLockRequestDto>(
                $"schedules/{scheduleId}/acquire-lock",
                new AcquireLockRequestDto { ScheduleId = scheduleId, WorkerId = workerId },
                cancellationToken);
            return result?.Acquired ?? false;
        }

        public Task ReleaseScheduleLockAsync(int scheduleId, string workerId, DateTime nextRunAt, Guid lastRunId, string lastStatus, CancellationToken cancellationToken)
            => PostAsync($"schedules/{scheduleId}/release-lock",
                new ReleaseLockRequestDto
                {
                    ScheduleId = scheduleId,
                    WorkerId   = workerId,
                    NextRunAt  = nextRunAt,
                    LastRunId  = lastRunId,
                    LastStatus = lastStatus
                },
                cancellationToken);

        public Task CreateWorkflowRunAsync(Guid runId, int workflowTypeId, int scheduleId, int maxRetries, CancellationToken cancellationToken)
            => PostAsync("runs",
                new CreateWorkflowRunRequestDto
                {
                    RunId          = runId,
                    WorkflowTypeId = workflowTypeId,
                    ScheduleId     = scheduleId,
                    MaxRetries     = maxRetries
                },
                cancellationToken);

        public Task UpsertStepRunAsync(Guid runId, int stepDefId, string status, int attempts, string? reason, string? artifactRef, DateTime? startedAt, DateTime? finishedAt, CancellationToken cancellationToken)
            => PostAsync($"runs/{runId}/steps/{stepDefId}",
                new UpsertStepRunRequestDto
                {
                    RunId       = runId,
                    StepDefId   = stepDefId,
                    Status      = status,
                    Attempts    = attempts,
                    Reason      = reason,
                    ArtifactRef = artifactRef,
                    StartedAt   = startedAt,
                    FinishedAt  = finishedAt
                },
                cancellationToken);

        public Task SetRunTerminalStatusAsync(Guid runId, string status, string? reason, DateTime? retryAt, int retryCount, CancellationToken cancellationToken)
            => PostAsync($"runs/{runId}/status",
                new SetRunStatusRequestDto
                {
                    RunId      = runId,
                    Status     = status,
                    Reason     = reason,
                    RetryAt    = retryAt,
                    RetryCount = retryCount
                },
                cancellationToken);

        public Task ResetRunForRetryAsync(Guid runId, CancellationToken cancellationToken)
            => PostAsync<object>($"runs/{runId}/reset-for-retry", new { }, cancellationToken);
    }
}
