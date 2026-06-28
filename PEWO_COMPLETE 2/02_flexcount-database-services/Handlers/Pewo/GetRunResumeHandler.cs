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

public class GetRunResumeHandler : IRequestHandler<GetRunResumeQuery, List<RunResumeStepDto>>
{
    private const string MethodName = "GetRunResumeHandler";

    private readonly ILogger<GetRunResumeHandler> _logger;
    private readonly IGenericRepository<RunResumeStepResult> _repository;

    public GetRunResumeHandler(
        ILogger<GetRunResumeHandler> logger,
        IUnitOfWork<RunResumeStepResult> unitOfWork)
    {
        _logger     = logger;
        _repository = unitOfWork.GetRepository();
    }

    public async Task<List<RunResumeStepDto>> Handle(GetRunResumeQuery request, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation(LoggingMessage.StartLogMessage, MethodName, JsonSerializer.Serialize(request));

            var parameters = new DynamicParameters();
            parameters.Add("@id_WorkflowRun",         request.Id_WorkflowRun,         DbType.Int32);
            parameters.Add("@id_CustomerWorkflowType", request.Id_CustomerWorkflowType, DbType.Int32);

            var results = await _repository.CustomQueryAsync(
                "EXEC dbo.usp_Pewo_GetRunResume @id_WorkflowRun, @id_CustomerWorkflowType",
                parameters,
                cancellationToken);

            var dtos = results.Adapt<List<RunResumeStepDto>>();

            _logger.LogInformation(LoggingMessage.EndLogMessage, MethodName, $"{dtos.Count} steps");

            return dtos;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, LoggingMessage.ErrorLogMessage, MethodName);
            throw;
        }
    }
}
