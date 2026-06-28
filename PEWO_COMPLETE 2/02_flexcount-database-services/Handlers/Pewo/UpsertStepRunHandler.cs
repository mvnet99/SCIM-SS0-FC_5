using Dapper;
using MediatR;
using Microsoft.Extensions.Logging;
using System.Data;
using System.Text.Json;
using WS.FC.Dapper.Domain.Entities;
using WS.FC.Dapper.Domain.Interfaces.Contexts;
using WS.FC.Dapper.Domain.Interfaces.Repositories;
using WS.FC.Dapper.Shared.Commands.Pewo;
using WS.FC.Dapper.Shared.Constants;

namespace WS.FC.Dapper.Application.Handlers.Pewo;

// ── UpsertStepRun ─────────────────────────────────────────────────────────────
// MERGE upsert — written once after each step completes.
public class UpsertStepRunHandler : IRequestHandler<UpsertStepRunCommand>
{
    private const string MethodName = "UpsertStepRunHandler";

    private readonly ILogger<UpsertStepRunHandler> _logger;
    private readonly IGenericRepository<WorkflowStepRun> _repository;

    public UpsertStepRunHandler(
        ILogger<UpsertStepRunHandler> logger,
        IUnitOfWork<WorkflowStepRun> unitOfWork)
    {
        _logger     = logger;
        _repository = unitOfWork.GetRepository();
    }

    public async Task Handle(UpsertStepRunCommand request, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation(LoggingMessage.StartLogMessage, MethodName, JsonSerializer.Serialize(request));

            var p = new DynamicParameters();
            p.Add("@id_WorkflowRun",     request.Id_WorkflowRun,     DbType.Int32);
            p.Add("@id_WorkflowStepDef", request.Id_WorkflowStepDef, DbType.Int32);
            p.Add("@Step_Kind",          request.Step_Kind,           DbType.String);
            p.Add("@Status",             request.Status,              DbType.String);
            p.Add("@Attempts",           request.Attempts,            DbType.Int16);
            p.Add("@Artifact_Ref",       request.Artifact_Ref,        DbType.String);
            p.Add("@Failure_Details",    request.Failure_Details,     DbType.String);
            p.Add("@Start_Time",         request.Start_Time,          DbType.DateTime);
            p.Add("@End_Time",           request.End_Time,            DbType.DateTime);

            await _repository.CustomQueryAsync(
                "EXEC dbo.usp_Pewo_UpsertStepRun @id_WorkflowRun, @id_WorkflowStepDef, @Step_Kind, @Status, @Attempts, @Artifact_Ref, @Failure_Details, @Start_Time, @End_Time",
                p,
                cancellationToken);

            _logger.LogInformation(LoggingMessage.EndNoInputLogMessage, MethodName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, LoggingMessage.ErrorLogMessage, MethodName);
            throw;
        }
    }
}
