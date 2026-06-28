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

public class InsertLogHandler : IRequestHandler<InsertLogCommand>
{
    private const string MethodName = "InsertLogHandler";

    private readonly ILogger<InsertLogHandler> _logger;
    private readonly IGenericRepository<WorkflowRunLog> _repository;

    public InsertLogHandler(
        ILogger<InsertLogHandler> logger,
        IUnitOfWork<WorkflowRunLog> unitOfWork)
    {
        _logger     = logger;
        _repository = unitOfWork.GetRepository();
    }

    public async Task Handle(InsertLogCommand request, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation(LoggingMessage.StartLogMessage, MethodName, JsonSerializer.Serialize(request));

            var p = new DynamicParameters();
            p.Add("@id_WorkflowRun", request.Id_WorkflowRun, DbType.Int32);
            p.Add("@id_Customer",    request.Id_Customer,     DbType.Int32);
            p.Add("@Customer_Name",  request.Customer_Name,   DbType.String);
            p.Add("@Step_Kind",      request.Step_Kind,       DbType.String);
            p.Add("@Log_Level",      request.Log_Level,       DbType.String);
            p.Add("@Message",        request.Message,         DbType.String);
            p.Add("@Event_Context",  request.Event_Context,   DbType.String);

            await _repository.CustomQueryAsync(
                "EXEC dbo.usp_Pewo_InsertLog @id_WorkflowRun, @id_Customer, @Customer_Name, @Step_Kind, @Log_Level, @Message, @Event_Context",
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
