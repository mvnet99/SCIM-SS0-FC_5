using Dapper;
using Mapster;
using MediatR;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using WS.FC.Dapper.Domain.Entities;
using WS.FC.Dapper.Domain.Interfaces.Contexts;
using WS.FC.Dapper.Domain.Interfaces.Repositories;
using WS.FC.Dapper.Shared.Constants;
using WS.FC.Dapper.Shared.DTOs.Pewo;
using WS.FC.Dapper.Shared.Queries.Pewo;

namespace WS.FC.Dapper.Application.Handlers.Pewo;

public class GetDueJobsHandler : IRequestHandler<GetDueJobsQuery, List<DueJobDto>>
{
    private const string MethodName = "GetDueJobsHandler";

    private readonly ILogger<GetDueJobsHandler> _logger;
    private readonly IGenericRepository<DueJobResult> _repository;

    public GetDueJobsHandler(
        ILogger<GetDueJobsHandler> logger,
        IUnitOfWork<DueJobResult> unitOfWork)
    {
        _logger     = logger;
        _repository = unitOfWork.GetRepository();
    }

    public async Task<List<DueJobDto>> Handle(GetDueJobsQuery request, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation(LoggingMessage.StartNoInputLogMessage, MethodName);

            var results = await _repository.CustomQueryAsync(
                "EXEC dbo.usp_Pewo_GetDueJobs",
                null,
                cancellationToken);

            var dtos = results.Adapt<List<DueJobDto>>();

            _logger.LogInformation(LoggingMessage.EndLogMessage, MethodName, $"{dtos.Count} due jobs");

            return dtos;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, LoggingMessage.ErrorLogMessage, MethodName);
            throw;
        }
    }
}
