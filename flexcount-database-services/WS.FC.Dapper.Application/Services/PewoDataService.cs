using MediatR;
using WS.FC.Dapper.Domain.Interfaces.Services;
using WS.FC.Dapper.Shared.Commands.Pewo;
using WS.FC.Dapper.Shared.DTOs.Pewo;

namespace WS.FC.Dapper.Application.Services
{
    public class PewoDataService : IPewoDataService
    {
        private readonly IMediator _mediator;

        public PewoDataService(IMediator mediator)
        {
            _mediator = mediator;
        }

        public Task<List<DueJobDto>> GetDueJobsAsync(CancellationToken cancellationToken)
            => _mediator.Send(new GetDueJobsQuery(), cancellationToken);

        public Task<List<RunResumeStepDto>> GetRunResumeAsync(Guid runId, int workflowTypeId, CancellationToken cancellationToken)
            => _mediator.Send(new GetRunResumeQuery(runId, workflowTypeId), cancellationToken);

        public Task<bool> AcquireScheduleLockAsync(int scheduleId, string workerId, CancellationToken cancellationToken)
            => _mediator.Send(new AcquireScheduleLockCommand(scheduleId, workerId), cancellationToken);

        public Task ReleaseScheduleLockAsync(int scheduleId, string workerId, DateTime nextRunAt, Guid lastRunId, string lastStatus, CancellationToken cancellationToken)
            => _mediator.Send(new ReleaseScheduleLockCommand(scheduleId, workerId, nextRunAt, lastRunId, lastStatus), cancellationToken);

        public Task CreateWorkflowRunAsync(Guid runId, int workflowTypeId, int scheduleId, int maxRetries, CancellationToken cancellationToken)
            => _mediator.Send(new CreateWorkflowRunCommand(runId, workflowTypeId, scheduleId, maxRetries), cancellationToken);

        public Task UpsertStepRunAsync(Guid runId, int stepDefId, string status, int attempts, string? reason, string? artifactRef, DateTime? startedAt, DateTime? finishedAt, CancellationToken cancellationToken)
            => _mediator.Send(new UpsertStepRunCommand(runId, stepDefId, status, attempts, reason, artifactRef, startedAt, finishedAt), cancellationToken);

        public Task SetRunTerminalStatusAsync(Guid runId, string status, string? reason, DateTime? retryAt, int retryCount, CancellationToken cancellationToken)
            => _mediator.Send(new SetRunTerminalStatusCommand(runId, status, reason, retryAt, retryCount), cancellationToken);

        public Task ResetRunForRetryAsync(Guid runId, CancellationToken cancellationToken)
            => _mediator.Send(new ResetRunForRetryCommand(runId), cancellationToken);
    }
}
