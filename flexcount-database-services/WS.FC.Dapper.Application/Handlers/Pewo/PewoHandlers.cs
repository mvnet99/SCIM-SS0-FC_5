using Dapper;
using MediatR;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using WS.FC.Dapper.Domain.Interfaces.Repositories;
using WS.FC.Dapper.Shared.Commands.Pewo;
using WS.FC.Dapper.Shared.Constants;
using WS.FC.Dapper.Shared.DTOs.Pewo;
using WS.FC.Dapper.Domain.Entities;

namespace WS.FC.Dapper.Application.Handlers.Pewo
{
    // ── GetDueJobs ────────────────────────────────────────────────────────────

    public class GetDueJobsHandler : IRequestHandler<GetDueJobsQuery, List<DueJobDto>>
    {
        private const string MethodName = "GetDueJobsHandler";
        private readonly ILogger<GetDueJobsHandler> _logger;
        private readonly IGenericRepository<PewoSchedule> _scheduleRepo;

        public GetDueJobsHandler(
            ILogger<GetDueJobsHandler> logger,
            IGenericRepository<PewoSchedule> scheduleRepo)
        {
            _logger = logger;
            _scheduleRepo = scheduleRepo;
        }

        public async Task<List<DueJobDto>> Handle(GetDueJobsQuery request, CancellationToken cancellationToken)
        {
            _logger.LogInformation(LoggingMessage.StartLogMessage, MethodName, "{}");
            var result = await _scheduleRepo.CustomQueryAsync(
                "EXEC workflow.usp_Pewo_GetDueJobs",
                null,
                cancellationToken);

            var list = result.Cast<dynamic>().Select(r => new DueJobDto
            {
                ScheduleId     = r.schedule_id,
                WorkflowTypeId = r.workflow_type_id,
                ScheduleName   = r.schedule_name,
                CronExpression = r.cron_expression,
                Timezone       = r.timezone,
                NextRunAt      = r.next_run_at,
                Status         = r.status,
                LastRunId      = r.last_run_id,
                WorkflowCode   = r.workflow_code,
                WorkflowName   = r.workflow_name,
                MaxRetries     = r.max_retries,
                RunId          = r.run_id,
                JobSource      = r.job_source
            }).ToList();

            _logger.LogInformation(LoggingMessage.EndLogMessage, MethodName, $"{list.Count} jobs");
            return list;
        }
    }

    // ── GetRunResume ─────────────────────────────────────────────────────────

    public class GetRunResumeHandler : IRequestHandler<GetRunResumeQuery, List<RunResumeStepDto>>
    {
        private const string MethodName = "GetRunResumeHandler";
        private readonly ILogger<GetRunResumeHandler> _logger;
        private readonly IGenericRepository<WorkflowStepDef> _stepDefRepo;

        public GetRunResumeHandler(
            ILogger<GetRunResumeHandler> logger,
            IGenericRepository<WorkflowStepDef> stepDefRepo)
        {
            _logger = logger;
            _stepDefRepo = stepDefRepo;
        }

        public async Task<List<RunResumeStepDto>> Handle(GetRunResumeQuery request, CancellationToken cancellationToken)
        {
            _logger.LogInformation(LoggingMessage.StartLogMessage, MethodName, JsonSerializer.Serialize(request));
            var p = new DynamicParameters();
            p.Add("@runId",           request.RunId);
            p.Add("@workflowTypeId",  request.WorkflowTypeId);

            var result = await _stepDefRepo.CustomQueryAsync(
                "EXEC workflow.usp_Pewo_GetRunResume @runId, @workflowTypeId",
                p,
                cancellationToken);

            var list = result.Cast<dynamic>().Select(r => new RunResumeStepDto
            {
                StepDefId      = r.step_def_id,
                StepOrder      = r.step_order,
                StepKind       = r.step_kind,
                StepName       = r.step_name,
                Config         = r.config,
                MaxAttempts    = r.max_attempts,
                BackoffSeconds = r.backoff_seconds,
                StepRunId      = r.step_run_id,
                Status         = r.status,
                Attempts       = r.attempts ?? 0,
                Reason         = r.reason,
                ArtifactRef    = r.artifact_ref
            }).ToList();

            _logger.LogInformation(LoggingMessage.EndLogMessage, MethodName, $"{list.Count} steps");
            return list;
        }
    }

    // ── AcquireScheduleLock ───────────────────────────────────────────────────

    public class AcquireScheduleLockHandler : IRequestHandler<AcquireScheduleLockCommand, bool>
    {
        private const string MethodName = "AcquireScheduleLockHandler";
        private readonly ILogger<AcquireScheduleLockHandler> _logger;
        private readonly IGenericRepository<PewoSchedule> _scheduleRepo;

        public AcquireScheduleLockHandler(
            ILogger<AcquireScheduleLockHandler> logger,
            IGenericRepository<PewoSchedule> scheduleRepo)
        {
            _logger = logger;
            _scheduleRepo = scheduleRepo;
        }

        public async Task<bool> Handle(AcquireScheduleLockCommand request, CancellationToken cancellationToken)
        {
            _logger.LogInformation(LoggingMessage.StartLogMessage, MethodName, JsonSerializer.Serialize(request));
            var p = new DynamicParameters();
            p.Add("@scheduleId", request.ScheduleId);
            p.Add("@workerId",   request.WorkerId);

            var result = await _scheduleRepo.CustomQueryAsync(
                "EXEC workflow.usp_Pewo_AcquireScheduleLock @scheduleId, @workerId",
                p,
                cancellationToken);

            var rows = result.Cast<dynamic>().FirstOrDefault()?.rows_affected ?? 0;
            _logger.LogInformation(LoggingMessage.EndLogMessage, MethodName, $"acquired={rows > 0}");
            return rows > 0;
        }
    }

    // ── ReleaseScheduleLock ───────────────────────────────────────────────────

    public class ReleaseScheduleLockHandler : IRequestHandler<ReleaseScheduleLockCommand>
    {
        private const string MethodName = "ReleaseScheduleLockHandler";
        private readonly ILogger<ReleaseScheduleLockHandler> _logger;
        private readonly IGenericRepository<PewoSchedule> _scheduleRepo;

        public ReleaseScheduleLockHandler(
            ILogger<ReleaseScheduleLockHandler> logger,
            IGenericRepository<PewoSchedule> scheduleRepo)
        {
            _logger = logger;
            _scheduleRepo = scheduleRepo;
        }

        public async Task Handle(ReleaseScheduleLockCommand request, CancellationToken cancellationToken)
        {
            _logger.LogInformation(LoggingMessage.StartLogMessage, MethodName, JsonSerializer.Serialize(request));
            var p = new DynamicParameters();
            p.Add("@scheduleId", request.ScheduleId);
            p.Add("@workerId",   request.WorkerId);
            p.Add("@nextRunAt",  request.NextRunAt);
            p.Add("@lastRunId",  request.LastRunId);
            p.Add("@lastStatus", request.LastStatus);

            await _scheduleRepo.CustomQueryAsync(
                "EXEC workflow.usp_Pewo_ReleaseScheduleLock @scheduleId, @workerId, @nextRunAt, @lastRunId, @lastStatus",
                p,
                cancellationToken);
            _logger.LogInformation(LoggingMessage.EndLogMessage, MethodName, "released");
        }
    }

    // ── CreateWorkflowRun ─────────────────────────────────────────────────────

    public class CreateWorkflowRunHandler : IRequestHandler<CreateWorkflowRunCommand>
    {
        private const string MethodName = "CreateWorkflowRunHandler";
        private readonly ILogger<CreateWorkflowRunHandler> _logger;
        private readonly IGenericRepository<WorkflowRun> _runRepo;

        public CreateWorkflowRunHandler(
            ILogger<CreateWorkflowRunHandler> logger,
            IGenericRepository<WorkflowRun> runRepo)
        {
            _logger = logger;
            _runRepo = runRepo;
        }

        public async Task Handle(CreateWorkflowRunCommand request, CancellationToken cancellationToken)
        {
            _logger.LogInformation(LoggingMessage.StartLogMessage, MethodName, JsonSerializer.Serialize(request));
            var run = new WorkflowRun
            {
                run_id           = request.RunId,
                workflow_type_id = request.WorkflowTypeId,
                schedule_id      = request.ScheduleId,
                status           = "PENDING",
                max_retries      = request.MaxRetries,
                created_at       = DateTime.UtcNow
            };
            await _runRepo.AddAsync("workflow", run, cancellationToken);
            _logger.LogInformation(LoggingMessage.EndLogMessage, MethodName, request.RunId.ToString());
        }
    }

    // ── UpsertStepRun ─────────────────────────────────────────────────────────

    public class UpsertStepRunHandler : IRequestHandler<UpsertStepRunCommand>
    {
        private const string MethodName = "UpsertStepRunHandler";
        private readonly ILogger<UpsertStepRunHandler> _logger;
        private readonly IGenericRepository<StepRun> _stepRunRepo;

        public UpsertStepRunHandler(
            ILogger<UpsertStepRunHandler> logger,
            IGenericRepository<StepRun> stepRunRepo)
        {
            _logger = logger;
            _stepRunRepo = stepRunRepo;
        }

        public async Task Handle(UpsertStepRunCommand request, CancellationToken cancellationToken)
        {
            _logger.LogInformation(LoggingMessage.StartLogMessage, MethodName, JsonSerializer.Serialize(request));
            var p = new DynamicParameters();
            p.Add("@runId",       request.RunId);
            p.Add("@stepDefId",   request.StepDefId);
            p.Add("@status",      request.Status);
            p.Add("@attempts",    request.Attempts);
            p.Add("@reason",      request.Reason);
            p.Add("@artifactRef", request.ArtifactRef);
            p.Add("@startedAt",   request.StartedAt);
            p.Add("@finishedAt",  request.FinishedAt);

            await _stepRunRepo.CustomQueryAsync(
                "EXEC workflow.usp_Pewo_UpsertStepRun @runId, @stepDefId, @status, @attempts, @reason, @artifactRef, @startedAt, @finishedAt",
                p,
                cancellationToken);
            _logger.LogInformation(LoggingMessage.EndLogMessage, MethodName, $"{request.RunId}/{request.StepDefId}={request.Status}");
        }
    }

    // ── SetRunTerminalStatus ──────────────────────────────────────────────────

    public class SetRunTerminalStatusHandler : IRequestHandler<SetRunTerminalStatusCommand>
    {
        private const string MethodName = "SetRunTerminalStatusHandler";
        private readonly ILogger<SetRunTerminalStatusHandler> _logger;
        private readonly IGenericRepository<WorkflowRun> _runRepo;

        public SetRunTerminalStatusHandler(
            ILogger<SetRunTerminalStatusHandler> logger,
            IGenericRepository<WorkflowRun> runRepo)
        {
            _logger = logger;
            _runRepo = runRepo;
        }

        public async Task Handle(SetRunTerminalStatusCommand request, CancellationToken cancellationToken)
        {
            _logger.LogInformation(LoggingMessage.StartLogMessage, MethodName, JsonSerializer.Serialize(request));
            var p = new DynamicParameters();
            p.Add("@runId",      request.RunId);
            p.Add("@status",     request.Status);
            p.Add("@reason",     request.Reason);
            p.Add("@retryAt",    request.RetryAt);
            p.Add("@retryCount", request.RetryCount);

            await _runRepo.CustomQueryAsync(
                "EXEC workflow.usp_Pewo_SetRunTerminalStatus @runId, @status, @reason, @retryAt, @retryCount",
                p,
                cancellationToken);
            _logger.LogInformation(LoggingMessage.EndLogMessage, MethodName, $"{request.RunId}={request.Status}");
        }
    }

    // ── ResetRunForRetry ──────────────────────────────────────────────────────

    public class ResetRunForRetryHandler : IRequestHandler<ResetRunForRetryCommand>
    {
        private const string MethodName = "ResetRunForRetryHandler";
        private readonly ILogger<ResetRunForRetryHandler> _logger;
        private readonly IGenericRepository<WorkflowRun> _runRepo;

        public ResetRunForRetryHandler(
            ILogger<ResetRunForRetryHandler> logger,
            IGenericRepository<WorkflowRun> runRepo)
        {
            _logger = logger;
            _runRepo = runRepo;
        }

        public async Task Handle(ResetRunForRetryCommand request, CancellationToken cancellationToken)
        {
            _logger.LogInformation(LoggingMessage.StartLogMessage, MethodName, request.RunId.ToString());
            var p = new DynamicParameters();
            p.Add("@runId", request.RunId);

            await _runRepo.CustomQueryAsync(
                "EXEC workflow.usp_Pewo_ResetRunForRetry @runId",
                p,
                cancellationToken);
            _logger.LogInformation(LoggingMessage.EndLogMessage, MethodName, request.RunId.ToString());
        }
    }
}
