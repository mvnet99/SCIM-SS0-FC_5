using Domain.ApiModels.Pewo;

namespace Domain.Services.Interfaces.Pewo
{
    public interface IPewoWorkerService
    {
        Task<WorkerRunResponse> RunAsync(CancellationToken cancellationToken);
    }

    public interface IPewoTotalsCheckService
    {
        Task<TotalsCheckResponse> CheckAsync(TotalsCheckRequest request, CancellationToken cancellationToken);
    }

    public interface IPewoJobDataService
    {
        // Due jobs
        Task<List<DueJobDto>> GetDueJobsAsync(CancellationToken cancellationToken);
        // Resume
        Task<List<RunResumeStepDto>> GetRunResumeAsync(Guid runId, int workflowTypeId, CancellationToken cancellationToken);
        // Lock
        Task<bool> AcquireScheduleLockAsync(int scheduleId, string workerId, CancellationToken cancellationToken);
        Task ReleaseScheduleLockAsync(int scheduleId, string workerId, DateTime nextRunAt, Guid lastRunId, string lastStatus, CancellationToken cancellationToken);
        // Run lifecycle
        Task CreateWorkflowRunAsync(Guid runId, int workflowTypeId, int scheduleId, int maxRetries, CancellationToken cancellationToken);
        Task UpsertStepRunAsync(Guid runId, int stepDefId, string status, int attempts, string? reason, string? artifactRef, DateTime? startedAt, DateTime? finishedAt, CancellationToken cancellationToken);
        Task SetRunTerminalStatusAsync(Guid runId, string status, string? reason, DateTime? retryAt, int retryCount, CancellationToken cancellationToken);
        Task ResetRunForRetryAsync(Guid runId, CancellationToken cancellationToken);
    }
}

// DTO aliases so the interface file is self-contained (mirror the shared DTOs)
namespace Domain.Services.Interfaces.Pewo
{
    public class DueJobDto
    {
        public int ScheduleId { get; set; }
        public int WorkflowTypeId { get; set; }
        public string ScheduleName { get; set; } = string.Empty;
        public string CronExpression { get; set; } = string.Empty;
        public string Timezone { get; set; } = "UTC";
        public DateTime? NextRunAt { get; set; }
        public string Status { get; set; } = string.Empty;
        public Guid? LastRunId { get; set; }
        public string WorkflowCode { get; set; } = string.Empty;
        public string WorkflowName { get; set; } = string.Empty;
        public int MaxRetries { get; set; }
        public Guid? RunId { get; set; }
        public string JobSource { get; set; } = "SCHEDULE";
    }

    public class RunResumeStepDto
    {
        public int StepDefId { get; set; }
        public int StepOrder { get; set; }
        public string StepKind { get; set; } = string.Empty;
        public string StepName { get; set; } = string.Empty;
        public string? Config { get; set; }
        public int MaxAttempts { get; set; }
        public int BackoffSeconds { get; set; }
        public Guid? StepRunId { get; set; }
        public string? Status { get; set; }
        public int Attempts { get; set; }
        public string? Reason { get; set; }
        public string? ArtifactRef { get; set; }
    }
}
