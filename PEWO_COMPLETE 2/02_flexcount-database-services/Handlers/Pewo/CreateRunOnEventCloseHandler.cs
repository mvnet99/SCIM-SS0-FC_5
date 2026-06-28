using Dapper;
using Mapster;
using MediatR;
using Microsoft.Extensions.Logging;
using System.Data;
using System.Text.Json;
using WS.FC.Dapper.Domain.Entities;
using WS.FC.Dapper.Domain.Interfaces.Contexts;
using WS.FC.Dapper.Domain.Interfaces.Repositories;
using WS.FC.Dapper.Shared.Commands.Pewo;
using WS.FC.Dapper.Shared.Constants;
using WS.FC.Dapper.Shared.DTOs.Pewo;

namespace WS.FC.Dapper.Application.Handlers.Pewo;

public class CreateRunOnEventCloseHandler : IRequestHandler<CreateRunOnEventCloseCommand, List<EventCloseResponseDto>>
{
    private const string MethodName = "CreateRunOnEventCloseHandler";

    private readonly ILogger<CreateRunOnEventCloseHandler> _logger;
    private readonly IGenericRepository<EventCloseResult> _repository;

    public CreateRunOnEventCloseHandler(
        ILogger<CreateRunOnEventCloseHandler> logger,
        IUnitOfWork<EventCloseResult> unitOfWork)
    {
        _logger     = logger;
        _repository = unitOfWork.GetRepository();
    }

    public async Task<List<EventCloseResponseDto>> Handle(CreateRunOnEventCloseCommand request, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation(LoggingMessage.StartLogMessage, MethodName, JsonSerializer.Serialize(request));

            var p = new DynamicParameters();
            p.Add("@id_Customer",          request.Id_Customer,          DbType.Int32);
            p.Add("@id_Event",             request.Id_Event,             DbType.Int32);
            p.Add("@Store_No",             request.Store_No,             DbType.String);
            p.Add("@Store_Name",           request.Store_Name,           DbType.String);
            p.Add("@Event_Date",           request.Event_Date,           DbType.DateTime);
            p.Add("@WorkflowType_Code",    request.WorkflowType_Code,    DbType.String);
            p.Add("@Event_Guid",           request.Event_Guid,           DbType.Guid);
            p.Add("@id_Store",             request.Id_Store,             DbType.Int32);
            p.Add("@Event_Status",         request.Event_Status,         DbType.String);
            p.Add("@Event_Scheduled_Date", request.Event_Scheduled_Date, DbType.DateTime);

            var results = await _repository.CustomQueryAsync(
                "EXEC dbo.usp_Pewo_CreateRunOnEventClose @id_Customer, @id_Event, @Store_No, @Store_Name, @Event_Date, @WorkflowType_Code, @Event_Guid, @id_Store, @Event_Status, @Event_Scheduled_Date",
                p,
                cancellationToken);

            var dtos = results.Adapt<List<EventCloseResponseDto>>();

            _logger.LogInformation(LoggingMessage.EndLogMessage, MethodName, $"{dtos.Count} runs created");

            return dtos;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, LoggingMessage.ErrorLogMessage, MethodName);
            throw;
        }
    }
}
