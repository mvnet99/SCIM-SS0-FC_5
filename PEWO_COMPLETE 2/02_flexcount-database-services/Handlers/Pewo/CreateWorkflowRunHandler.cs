using Mapster;
using MediatR;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using WS.FC.Dapper.Domain.Entities;
using WS.FC.Dapper.Domain.Interfaces.Contexts;
using WS.FC.Dapper.Domain.Interfaces.Repositories;
using WS.FC.Dapper.Shared.Commands.Pewo;
using WS.FC.Dapper.Shared.Constants;

namespace WS.FC.Dapper.Application.Handlers.Pewo;

public class CreateWorkflowRunHandler : IRequestHandler<CreateWorkflowRunCommand, int>
{
    private const string MethodName   = "CreateWorkflowRunHandler";
    private const string PewoSchema   = "dbo";

    private readonly ILogger<CreateWorkflowRunHandler> _logger;
    private readonly IGenericRepository<WorkflowRun> _repository;

    public CreateWorkflowRunHandler(
        ILogger<CreateWorkflowRunHandler> logger,
        IUnitOfWork<WorkflowRun> unitOfWork)
    {
        _logger     = logger;
        _repository = unitOfWork.GetRepository();
    }

    public async Task<int> Handle(CreateWorkflowRunCommand request, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation(LoggingMessage.StartLogMessage, MethodName, JsonSerializer.Serialize(request));

            var entity = new WorkflowRun
            {
                id_Schedule             = request.Id_Schedule,
                id_CustomerWorkflowType = request.Id_CustomerWorkflowType,
                Status                  = "PENDING",
                Retry_Count             = 0,
                Max_Retries             = request.Max_Retries,
                created_date            = DateTime.UtcNow,
                last_updated_date       = DateTime.UtcNow
            };

            var newId = await _repository.AddAndReturnIdAsync(PewoSchema, entity, cancellationToken);

            _logger.LogInformation(LoggingMessage.EndLogMessage, MethodName, $"NewRunId={newId}");

            return newId;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, LoggingMessage.ErrorLogMessage, MethodName);
            throw;
        }
    }
}
