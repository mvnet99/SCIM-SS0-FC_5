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

public class SetRunTerminalStatusHandler : IRequestHandler<SetRunTerminalStatusCommand>
{
    private const string MethodName = "SetRunTerminalStatusHandler";

    private readonly ILogger<SetRunTerminalStatusHandler> _logger;
    private readonly IGenericRepository<WorkflowRun> _repository;

    public SetRunTerminalStatusHandler(
        ILogger<SetRunTerminalStatusHandler> logger,
        IUnitOfWork<WorkflowRun> unitOfWork)
    {
        _logger     = logger;
        _repository = unitOfWork.GetRepository();
    }

    public async Task Handle(SetRunTerminalStatusCommand request, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation(LoggingMessage.StartLogMessage, MethodName, JsonSerializer.Serialize(request));

            var p = new DynamicParameters();
            p.Add("@id_WorkflowRun", request.Id_WorkflowRun, DbType.Int32);
            p.Add("@Status",         request.Status,          DbType.String);
            p.Add("@Reason",         request.Reason,          DbType.String);
            p.Add("@Retry_At",       request.Retry_At,        DbType.DateTime);
            p.Add("@Retry_Count",    request.Retry_Count,     DbType.Int16);

            await _repository.CustomQueryAsync(
                "EXEC dbo.usp_Pewo_SetRunTerminalStatus @id_WorkflowRun, @Status, @Reason, @Retry_At, @Retry_Count",
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
