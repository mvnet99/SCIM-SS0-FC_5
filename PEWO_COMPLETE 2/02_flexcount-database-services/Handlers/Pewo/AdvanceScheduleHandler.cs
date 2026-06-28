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

public class AdvanceScheduleHandler : IRequestHandler<AdvanceScheduleCommand>
{
    private const string MethodName = "AdvanceScheduleHandler";

    private readonly ILogger<AdvanceScheduleHandler> _logger;
    private readonly IGenericRepository<PewoSchedule> _repository;

    public AdvanceScheduleHandler(
        ILogger<AdvanceScheduleHandler> logger,
        IUnitOfWork<PewoSchedule> unitOfWork)
    {
        _logger     = logger;
        _repository = unitOfWork.GetRepository();
    }

    public async Task Handle(AdvanceScheduleCommand request, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation(LoggingMessage.StartLogMessage, MethodName, JsonSerializer.Serialize(request));

            var p = new DynamicParameters();
            p.Add("@id_Schedule",  request.Id_Schedule,  DbType.Int32);
            p.Add("@Next_Run_At",  request.Next_Run_At,  DbType.DateTime);
            p.Add("@Last_Run_Id",  request.Last_Run_Id,  DbType.Int32);
            p.Add("@Last_Status",  request.Last_Status,  DbType.String);

            await _repository.CustomQueryAsync(
                "EXEC dbo.usp_Pewo_AdvanceSchedule @id_Schedule, @Next_Run_At, @Last_Run_Id, @Last_Status",
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
