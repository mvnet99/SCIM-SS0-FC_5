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

public class ResetRunForRetryHandler : IRequestHandler<ResetRunForRetryCommand>
{
    private const string MethodName = "ResetRunForRetryHandler";

    private readonly ILogger<ResetRunForRetryHandler> _logger;
    private readonly IGenericRepository<WorkflowRun> _repository;

    public ResetRunForRetryHandler(
        ILogger<ResetRunForRetryHandler> logger,
        IUnitOfWork<WorkflowRun> unitOfWork)
    {
        _logger     = logger;
        _repository = unitOfWork.GetRepository();
    }

    public async Task Handle(ResetRunForRetryCommand request, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation(LoggingMessage.StartLogMessage, MethodName, JsonSerializer.Serialize(request));

            var p = new DynamicParameters();
            p.Add("@id_WorkflowRun", request.Id_WorkflowRun, DbType.Int32);

            await _repository.CustomQueryAsync(
                "EXEC dbo.usp_Pewo_ResetRunForRetry @id_WorkflowRun",
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
