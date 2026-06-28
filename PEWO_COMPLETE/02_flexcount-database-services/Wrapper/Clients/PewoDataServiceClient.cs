using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using WS.FC.DatabaseService.Wrapper.Interfaces;
using WS.FC.DatabaseService.Wrapper.ServiceClients.Base;
using WS.FC.Dapper.Shared.DTOs.Pewo;

namespace WS.FC.DatabaseService.Wrapper.ServiceClients;

public class PewoDataServiceClient : BaseServiceClient, IPewoDataServiceClient
{
    public PewoDataServiceClient(HttpClient httpClient) : base(httpClient) { }

    public Task<List<DueJobDto>> GetDueJobsAsync(CancellationToken cancellationToken)
        => GetListAsync<DueJobDto>("due-jobs", cancellationToken);

    public Task<List<RunResumeStepDto>> GetRunResumeAsync(int idWorkflowRun, int idCustomerWorkflowType, CancellationToken cancellationToken)
        => GetListAsync<RunResumeStepDto>($"runs/{idWorkflowRun}/resume?idCustomerWorkflowType={idCustomerWorkflowType}", cancellationToken);

    public Task<List<WorkflowRunEventDto>> GetWorkflowRunEventsAsync(int idWorkflowRun, CancellationToken cancellationToken)
        => GetListAsync<WorkflowRunEventDto>($"runs/{idWorkflowRun}/events", cancellationToken);

    public Task<List<BatchRunStatusDto>> GetBatchRunStatusAsync(string workflowTypeCode, string batchKey, CancellationToken cancellationToken)
        => GetListAsync<BatchRunStatusDto>($"batch-status?workflowTypeCode={workflowTypeCode}&batchKey={Uri.EscapeDataString(batchKey)}", cancellationToken);

    public async Task<int> CreateWorkflowRunAsync(int idSchedule, int idCustomerWorkflowType, short maxRetries, CancellationToken cancellationToken)
    {
        var result = await PostAsJsonAndDeserializeItemAsync<int, CreateWorkflowRunDto>(
            "runs",
            new CreateWorkflowRunDto
            {
                Id_Schedule             = idSchedule,
                Id_CustomerWorkflowType = idCustomerWorkflowType,
                Max_Retries             = maxRetries
            },
            cancellationToken);
        return result;
    }

    public async Task<int> CreateWorkflowRunEventAsync(int idWorkflowRun, int? idEvent, int? idCustomer, int? idStore,
        string? storeNo, string? storeName, Guid? eventGuid, string? eventStatus,
        DateTime? eventScheduledDate, DateTime? eventDate, string? metadataJson,
        CancellationToken cancellationToken)
    {
        var result = await PostAsJsonAndDeserializeItemAsync<int, CreateWorkflowRunEventDto>(
            $"runs/{idWorkflowRun}/events",
            new CreateWorkflowRunEventDto
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
            },
            cancellationToken);
        return result;
    }

    public Task UpsertStepRunAsync(int idWorkflowRun, int idWorkflowStepDef, string stepKind, string status,
        short attempts, string? artifactRef, string? failureDetails, DateTime? startTime, DateTime? endTime,
        CancellationToken cancellationToken)
        => PostAsync($"runs/{idWorkflowRun}/steps/{idWorkflowStepDef}",
            new UpsertStepRunDto
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
            },
            cancellationToken);

    public Task SetRunTerminalStatusAsync(int idWorkflowRun, string status, string? reason,
        DateTime? retryAt, short retryCount, CancellationToken cancellationToken)
        => PutAsync($"runs/{idWorkflowRun}/status",
            new SetRunTerminalStatusDto
            {
                Status      = status,
                Reason      = reason,
                Retry_At    = retryAt,
                Retry_Count = retryCount
            },
            cancellationToken);

    public Task AdvanceScheduleAsync(int idSchedule, DateTime nextRunAt, int lastRunId, string lastStatus,
        CancellationToken cancellationToken)
        => PutAsync($"schedules/{idSchedule}/advance",
            new AdvanceScheduleDto
            {
                Next_Run_At  = nextRunAt,
                Last_Run_Id  = lastRunId,
                Last_Status  = lastStatus
            },
            cancellationToken);

    public Task ResetRunForRetryAsync(int idWorkflowRun, CancellationToken cancellationToken)
        => PutAsync($"runs/{idWorkflowRun}/reset", new { }, cancellationToken);

    public Task InsertLogAsync(int idWorkflowRun, int? idCustomer, string? customerName, string? stepKind,
        string logLevel, string message, string? eventContext, CancellationToken cancellationToken)
        => PostAsync("logs",
            new InsertLogDto
            {
                Id_Customer   = idCustomer,
                Customer_Name = customerName,
                Step_Kind     = stepKind,
                Log_Level     = logLevel,
                Message       = message,
                Event_Context = eventContext
            },
            cancellationToken);

    public Task<List<EventCloseResponseDto>> CreateRunOnEventCloseAsync(int idCustomer, int idEvent,
        string storeNo, string? storeName, DateTime eventDate, string? workflowTypeCode,
        Guid? eventGuid, int? idStore, string? eventStatus, DateTime? eventScheduledDate,
        CancellationToken cancellationToken)
        => PostAsJsonAndDeserializeListAsync<EventCloseResponseDto, CreateRunOnEventCloseDto>(
            "event-close",
            new CreateRunOnEventCloseDto
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
            },
            cancellationToken);
}
