using WS.FC.Dapper.Shared.DTOs.Pewo;

namespace WS.FC.Dapper.Domain.Interfaces.Services
{
    public interface IPewoDataService
    {
        Task<List<DueJobDto>> GetDueJobsAsync(CancellationToken cancellationToken);
        Task<List<RunResumeStepDto>> GetRunResumeAsync(Guid runId, int workflowTypeId, CancellationToken cancellationToken);
        Task<bool> AcquireScheduleLockAsync(int scheduleId, string workerId, CancellationToken cancellationToken);
        Task ReleaseScheduleLockAsync(int scheduleId, string workerId, DateTime nextRunAt, Guid lastRunId, string lastStatus, CancellationToken cancellationToken);
        Task CreateWorkflowRunAsync(Guid runId, int workflowTypeId, int scheduleId, int maxRetries, CancellationToken cancellationToken);
        Task UpsertStepRunAsync(Guid runId, int stepDefId, string status, int attempts, string? reason, string? artifactRef, DateTime? startedAt, DateTime? finishedAt, CancellationToken cancellationToken);
        Task SetRunTerminalStatusAsync(Guid runId, string status, string? reason, DateTime? retryAt, int retryCount, CancellationToken cancellationToken);
        Task ResetRunForRetryAsync(Guid runId, CancellationToken cancellationToken);
    }
}
