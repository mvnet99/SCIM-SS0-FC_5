using MediatR;
using WS.FC.Dapper.Shared.DTOs.Pewo;

namespace WS.FC.Dapper.Shared.Commands.Pewo
{
    public record GetDueJobsQuery() : IRequest<List<DueJobDto>>;

    public record GetRunResumeQuery(Guid RunId, int WorkflowTypeId) : IRequest<List<RunResumeStepDto>>;

    public record AcquireScheduleLockCommand(int ScheduleId, string WorkerId) : IRequest<bool>;

    public record ReleaseScheduleLockCommand(
        int ScheduleId,
        string WorkerId,
        DateTime NextRunAt,
        Guid LastRunId,
        string LastStatus) : IRequest;

    public record CreateWorkflowRunCommand(
        Guid RunId,
        int WorkflowTypeId,
        int ScheduleId,
        int MaxRetries) : IRequest;

    public record UpsertStepRunCommand(
        Guid RunId,
        int StepDefId,
        string Status,
        int Attempts,
        string? Reason,
        string? ArtifactRef,
        DateTime? StartedAt,
        DateTime? FinishedAt) : IRequest;

    public record SetRunTerminalStatusCommand(
        Guid RunId,
        string Status,
        string? Reason,
        DateTime? RetryAt,
        int RetryCount) : IRequest;

    public record ResetRunForRetryCommand(Guid RunId) : IRequest;
}
