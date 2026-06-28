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

public class GetBatchRunStatusHandler : IRequestHandler<GetBatchRunStatusQuery, List<BatchRunStatusDto>>
{
    private const string MethodName = "GetBatchRunStatusHandler";

    private readonly ILogger<GetBatchRunStatusHandler> _logger;
    private readonly IGenericRepository<BatchRunStatusResult> _repository;

    public GetBatchRunStatusHandler(
        ILogger<GetBatchRunStatusHandler> logger,
        IUnitOfWork<BatchRunStatusResult> unitOfWork)
    {
        _logger     = logger;
        _repository = unitOfWork.GetRepository();
    }

    public async Task<List<BatchRunStatusDto>> Handle(GetBatchRunStatusQuery request, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation(LoggingMessage.StartLogMessage, MethodName, JsonSerializer.Serialize(request));

            var parameters = new DynamicParameters();
            parameters.Add("@WorkflowTypeCode", request.WorkflowTypeCode, DbType.String);
            parameters.Add("@BatchKey",          request.BatchKey,          DbType.String);

            var results = await _repository.CustomQueryAsync(
                "EXEC dbo.usp_Pewo_GetBatchRunStatus @WorkflowTypeCode, @BatchKey",
                parameters,
                cancellationToken);

            var dtos = results.Adapt<List<BatchRunStatusDto>>();

            _logger.LogInformation(LoggingMessage.EndLogMessage, MethodName, $"{dtos.Count} batch runs");

            return dtos;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, LoggingMessage.ErrorLogMessage, MethodName);
            throw;
        }
    }
}
