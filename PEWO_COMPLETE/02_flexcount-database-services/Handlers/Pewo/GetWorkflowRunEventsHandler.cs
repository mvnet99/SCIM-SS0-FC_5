using Dapper;
using Mapster;
using MediatR;
using Microsoft.Extensions.Logging;
using System.Data;
using System.Text.Json;
using WS.FC.Dapper.Domain.Entities;
using WS.FC.Dapper.Domain.Interfaces.Contexts;
using WS.FC.Dapper.Domain.Interfaces.Repositories;
using WS.FC.Dapper.Shared.Constants;
using WS.FC.Dapper.Shared.DTOs.Pewo;
using WS.FC.Dapper.Shared.Queries.Pewo;

namespace WS.FC.Dapper.Application.Handlers.Pewo;

public class GetWorkflowRunEventsHandler : IRequestHandler<GetWorkflowRunEventsQuery, List<WorkflowRunEventDto>>
{
    private const string MethodName = "GetWorkflowRunEventsHandler";

    private readonly ILogger<GetWorkflowRunEventsHandler> _logger;
    private readonly IGenericRepository<WorkflowRunEvent> _repository;

    public GetWorkflowRunEventsHandler(
        ILogger<GetWorkflowRunEventsHandler> logger,
        IUnitOfWork<WorkflowRunEvent> unitOfWork)
    {
        _logger     = logger;
        _repository = unitOfWork.GetRepository();
    }

    public async Task<List<WorkflowRunEventDto>> Handle(GetWorkflowRunEventsQuery request, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation(LoggingMessage.StartLogMessage, MethodName, JsonSerializer.Serialize(request));

            var parameters = new DynamicParameters();
            parameters.Add("@id_WorkflowRun", request.Id_WorkflowRun, DbType.Int32);

            var results = await _repository.CustomQueryAsync(
                "EXEC dbo.usp_Pewo_GetWorkflowRunEvents @id_WorkflowRun",
                parameters,
                cancellationToken);

            var dtos = results.Adapt<List<WorkflowRunEventDto>>();

            _logger.LogInformation(LoggingMessage.EndLogMessage, MethodName, $"{dtos.Count} events");

            return dtos;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, LoggingMessage.ErrorLogMessage, MethodName);
            throw;
        }
    }
}
