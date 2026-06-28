using MediatR;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using WS.FC.Dapper.Domain.Entities;
using WS.FC.Dapper.Domain.Interfaces.Contexts;
using WS.FC.Dapper.Domain.Interfaces.Repositories;
using WS.FC.Dapper.Shared.Commands.Pewo;
using WS.FC.Dapper.Shared.Constants;

namespace WS.FC.Dapper.Application.Handlers.Pewo;

public class CreateWorkflowRunEventHandler : IRequestHandler<CreateWorkflowRunEventCommand, int>
{
    private const string MethodName = "CreateWorkflowRunEventHandler";
    private const string PewoSchema = "dbo";

    private readonly ILogger<CreateWorkflowRunEventHandler> _logger;
    private readonly IGenericRepository<WorkflowRunEvent> _repository;

    public CreateWorkflowRunEventHandler(
        ILogger<CreateWorkflowRunEventHandler> logger,
        IUnitOfWork<WorkflowRunEvent> unitOfWork)
    {
        _logger     = logger;
        _repository = unitOfWork.GetRepository();
    }

    public async Task<int> Handle(CreateWorkflowRunEventCommand request, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation(LoggingMessage.StartLogMessage, MethodName, JsonSerializer.Serialize(request));

            var entity = new WorkflowRunEvent
            {
                id_WorkflowRun        = request.Id_WorkflowRun,
                id_Event              = request.Id_Event,
                id_Customer           = request.Id_Customer,
                id_Store              = request.Id_Store,
                Store_No              = request.Store_No,
                Store_Name            = request.Store_Name,
                Event_Guid            = request.Event_Guid,
                Event_Status          = request.Event_Status,
                Event_Scheduled_Date  = request.Event_Scheduled_Date,
                Event_Date            = request.Event_Date,
                Metadata_Json         = request.Metadata_Json,
                created_date          = DateTime.UtcNow
            };

            var newId = await _repository.AddAndReturnIdAsync(PewoSchema, entity, cancellationToken);

            _logger.LogInformation(LoggingMessage.EndLogMessage, MethodName, $"NewEventRowId={newId}");

            return newId;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, LoggingMessage.ErrorLogMessage, MethodName);
            throw;
        }
    }
}
