using MediatR;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WS.FC.Dapper.Domain.Interfaces.Services;
using WS.FC.Dapper.Shared.Commands.Pewo;
using WS.FC.Dapper.Shared.Constants;
using WS.FC.Dapper.Shared.DTOs.Pewo;
using WS.FC.Dapper.Shared.Queries.Pewo;

namespace WS.FC.Dapper.Application.Services;

public class PewoDataService : IPewoDataService
{
    private const string MethodName = "PewoDataService";

    private readonly ILogger<PewoDataService> _logger;
    private readonly IMediator _mediator;

    public PewoDataService(ILogger<PewoDataService> logger, IMediator mediator)
    {
        _logger   = logger;
        _mediator = mediator;
    }

    public async Task<List<DueJobDto>> GetDueJobsAsync(CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation(LoggingMessage.StartNoInputLogMessage, MethodName);
            var result = await _mediator.Send(new GetDueJobsQuery(), cancellationToken);
            _logger.LogInformation(LoggingMessage.EndLogMessage, MethodName, $"{result.Count} due jobs");
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, LoggingMessage.ErrorLogMessage, MethodName);
            throw;
        }
    }

    public async Task<List<RunResumeStepDto>> GetRunResumeAsync(int idWorkflowRun, int idCustomerWorkflowType, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation(LoggingMessage.StartLogMessage, MethodName, $"RunId={idWorkflowRun}");
            var result = await _mediator.Send(new GetRunResumeQuery
            {
                Id_WorkflowRun          = idWorkflowRun,
                Id_CustomerWorkflowType = idCustomerWorkflowType
            }, cancellationToken);
            _logger.LogInformation(LoggingMessage.EndLogMessage, MethodName, $"{result.Count} steps");
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, LoggingMessage.ErrorLogMessage, MethodName);
            throw;
        }
    }

    public async Task<List<WorkflowRunEventDto>> GetWorkflowRunEventsAsync(int idWorkflowRun, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation(LoggingMessage.StartLogMessage, MethodName, $"RunId={idWorkflowRun}");
            var result = await _mediator.Send(new GetWorkflowRunEventsQuery { Id_WorkflowRun = idWorkflowRun }, cancellationToken);
            _logger.LogInformation(LoggingMessage.EndLogMessage, MethodName, $"{result.Count} events");
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, LoggingMessage.ErrorLogMessage, MethodName);
            throw;
        }
    }

    public async Task<List<BatchRunStatusDto>> GetBatchRunStatusAsync(string workflowTypeCode, string batchKey, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation(LoggingMessage.StartLogMessage, MethodName, $"BatchKey={batchKey}");
            var result = await _mediator.Send(new GetBatchRunStatusQuery
            {
                WorkflowTypeCode = workflowTypeCode,
                BatchKey         = batchKey
            }, cancellationToken);
            _logger.LogInformation(LoggingMessage.EndLogMessage, MethodName, $"{result.Count} batch entries");
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, LoggingMessage.ErrorLogMessage, MethodName);
            throw;
        }
    }

    public async Task<int> CreateWorkflowRunAsync(int idSchedule, int idCustomerWorkflowType, short maxRetries, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation(LoggingMessage.StartLogMessage, MethodName, $"ScheduleId={idSchedule}");
            var newId = await _mediator.Send(new CreateWorkflowRunCommand
            {
                Id_Schedule             = idSchedule,
                Id_CustomerWorkflowType = idCustomerWorkflowType,
                Max_Retries             = maxRetries
            }, cancellationToken);
            _logger.LogInformation(LoggingMessage.EndLogMessage, MethodName, $"NewRunId={newId}");
            return newId;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, LoggingMessage.ErrorLogMessage, MethodName);
            throw;
        }
    }

    public async Task<int> CreateWorkflowRunEventAsync(int idWorkflowRun, int? idEvent, int? idCustomer, int? idStore,
        string? storeNo, string? storeName, Guid? eventGuid, string? eventStatus,
        DateTime? eventScheduledDate, DateTime? eventDate, string? metadataJson, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation(LoggingMessage.StartLogMessage, MethodName, $"RunId={idWorkflowRun}");
            var newId = await _mediator.Send(new CreateWorkflowRunEventCommand
            {
                Id_WorkflowRun       = idWorkflowRun,
                Id_Event             = idEvent,
                Id_Customer          = idCustomer,
                Id_Store             = idStore,
                Store_No             = storeNo,
                Store_Name           = storeName,
                Event_Guid           = eventGuid,
                Event_Status         = eventStatus,
                Event_Scheduled_Date = eventScheduledDate,
                Event_Date           = eventDate,
                Metadata_Json        = metadataJson
            }, cancellationToken);
            _logger.LogInformation(LoggingMessage.EndLogMessage, MethodName, $"NewEventRowId={newId}");
            return newId;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, LoggingMessage.ErrorLogMessage, MethodName);
            throw;
        }
    }

    public async Task UpsertStepRunAsync(int idWorkflowRun, int idWorkflowStepDef, string stepKind, string status,
        short attempts, string? artifactRef, string? failureDetails, DateTime? startTime, DateTime? endTime,
        CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation(LoggingMessage.StartLogMessage, MethodName, $"RunId={idWorkflowRun} Step={stepKind}");
            await _mediator.Send(new UpsertStepRunCommand
            {
                Id_WorkflowRun     = idWorkflowRun,
                Id_WorkflowStepDef = idWorkflowStepDef,
                Step_Kind          = stepKind,
                Status             = status,
                Attempts           = attempts,
                Artifact_Ref       = artifactRef,
                Failure_Details    = failureDetails,
                Start_Time         = startTime,
                End_Time           = endTime
            }, cancellationToken);
            _logger.LogInformation(LoggingMessage.EndNoInputLogMessage, MethodName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, LoggingMessage.ErrorLogMessage, MethodName);
            throw;
        }
    }

    public async Task SetRunTerminalStatusAsync(int idWorkflowRun, string status, string? reason,
        DateTime? retryAt, short retryCount, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation(LoggingMessage.StartLogMessage, MethodName, $"RunId={idWorkflowRun} Status={status}");
            await _mediator.Send(new SetRunTerminalStatusCommand
            {
                Id_WorkflowRun = idWorkflowRun,
                Status         = status,
                Reason         = reason,
                Retry_At       = retryAt,
                Retry_Count    = retryCount
            }, cancellationToken);
            _logger.LogInformation(LoggingMessage.EndNoInputLogMessage, MethodName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, LoggingMessage.ErrorLogMessage, MethodName);
            throw;
        }
    }

    public async Task AdvanceScheduleAsync(int idSchedule, DateTime nextRunAt, int lastRunId, string lastStatus,
        CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation(LoggingMessage.StartLogMessage, MethodName, $"ScheduleId={idSchedule}");
            await _mediator.Send(new AdvanceScheduleCommand
            {
                Id_Schedule  = idSchedule,
                Next_Run_At  = nextRunAt,
                Last_Run_Id  = lastRunId,
                Last_Status  = lastStatus
            }, cancellationToken);
            _logger.LogInformation(LoggingMessage.EndNoInputLogMessage, MethodName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, LoggingMessage.ErrorLogMessage, MethodName);
            throw;
        }
    }

    public async Task ResetRunForRetryAsync(int idWorkflowRun, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation(LoggingMessage.StartLogMessage, MethodName, $"RunId={idWorkflowRun}");
            await _mediator.Send(new ResetRunForRetryCommand { Id_WorkflowRun = idWorkflowRun }, cancellationToken);
            _logger.LogInformation(LoggingMessage.EndNoInputLogMessage, MethodName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, LoggingMessage.ErrorLogMessage, MethodName);
            throw;
        }
    }

    public async Task InsertLogAsync(int idWorkflowRun, int? idCustomer, string? customerName, string? stepKind,
        string logLevel, string message, string? eventContext, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation(LoggingMessage.StartLogMessage, MethodName, $"RunId={idWorkflowRun}");
            await _mediator.Send(new InsertLogCommand
            {
                Id_WorkflowRun = idWorkflowRun,
                Id_Customer    = idCustomer,
                Customer_Name  = customerName,
                Step_Kind      = stepKind,
                Log_Level      = logLevel,
                Message        = message,
                Event_Context  = eventContext
            }, cancellationToken);
            _logger.LogInformation(LoggingMessage.EndNoInputLogMessage, MethodName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, LoggingMessage.ErrorLogMessage, MethodName);
            throw;
        }
    }

    public async Task<List<EventCloseResponseDto>> CreateRunOnEventCloseAsync(int idCustomer, int idEvent,
        string storeNo, string? storeName, DateTime eventDate, string? workflowTypeCode,
        Guid? eventGuid, int? idStore, string? eventStatus, DateTime? eventScheduledDate,
        CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation(LoggingMessage.StartLogMessage, MethodName, $"Customer={idCustomer} Event={idEvent}");
            var result = await _mediator.Send(new CreateRunOnEventCloseCommand
            {
                Id_Customer          = idCustomer,
                Id_Event             = idEvent,
                Store_No             = storeNo,
                Store_Name           = storeName,
                Event_Date           = eventDate,
                WorkflowType_Code    = workflowTypeCode,
                Event_Guid           = eventGuid,
                Id_Store             = idStore,
                Event_Status         = eventStatus,
                Event_Scheduled_Date = eventScheduledDate
            }, cancellationToken);
            _logger.LogInformation(LoggingMessage.EndLogMessage, MethodName, $"{result.Count} runs created");
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, LoggingMessage.ErrorLogMessage, MethodName);
            throw;
        }
    }
}
