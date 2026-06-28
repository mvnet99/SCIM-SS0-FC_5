using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using WS.FC.DatabaseService.Wrapper.Interfaces.Base;
using WS.FC.Dapper.Shared.DTOs.Pewo;

namespace WS.FC.DatabaseService.Wrapper.Interfaces;

public interface IPewoDataServiceClient : IBaseServiceClient
{
    Task<List<DueJobDto>> GetDueJobsAsync(CancellationToken cancellationToken);
    Task<List<RunResumeStepDto>> GetRunResumeAsync(int idWorkflowRun, int idCustomerWorkflowType, CancellationToken cancellationToken);
    Task<List<WorkflowRunEventDto>> GetWorkflowRunEventsAsync(int idWorkflowRun, CancellationToken cancellationToken);
    Task<List<BatchRunStatusDto>> GetBatchRunStatusAsync(string workflowTypeCode, string batchKey, CancellationToken cancellationToken);
    Task<int> CreateWorkflowRunAsync(int idSchedule, int idCustomerWorkflowType, short maxRetries, CancellationToken cancellationToken);
    Task<int> CreateWorkflowRunEventAsync(int idWorkflowRun, int? idEvent, int? idCustomer, int? idStore, string? storeNo, string? storeName, Guid? eventGuid, string? eventStatus, DateTime? eventScheduledDate, DateTime? eventDate, string? metadataJson, CancellationToken cancellationToken);
    Task UpsertStepRunAsync(int idWorkflowRun, int idWorkflowStepDef, string stepKind, string status, short attempts, string? artifactRef, string? failureDetails, DateTime? startTime, DateTime? endTime, CancellationToken cancellationToken);
    Task SetRunTerminalStatusAsync(int idWorkflowRun, string status, string? reason, DateTime? retryAt, short retryCount, CancellationToken cancellationToken);
    Task AdvanceScheduleAsync(int idSchedule, DateTime nextRunAt, int lastRunId, string lastStatus, CancellationToken cancellationToken);
    Task ResetRunForRetryAsync(int idWorkflowRun, CancellationToken cancellationToken);
    Task InsertLogAsync(int idWorkflowRun, int? idCustomer, string? customerName, string? stepKind, string logLevel, string message, string? eventContext, CancellationToken cancellationToken);
    Task<List<EventCloseResponseDto>> CreateRunOnEventCloseAsync(int idCustomer, int idEvent, string storeNo, string? storeName, DateTime eventDate, string? workflowTypeCode, Guid? eventGuid, int? idStore, string? eventStatus, DateTime? eventScheduledDate, CancellationToken cancellationToken);
}
