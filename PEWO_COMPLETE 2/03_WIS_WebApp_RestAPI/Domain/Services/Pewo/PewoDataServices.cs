using Domain.ApiModels.Pewo;
using Domain.Services.Interfaces.Pewo;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WS.FC.DatabaseService.Wrapper.Interfaces;
using WS.FC.Dapper.Shared.DTOs.Pewo;

namespace Domain.Services.Pewo;

// ═════════════════════════════════════════════════════════════════════════════
// PewoJobDataService — thin adapter over IPewoDataServiceClient (DB Services wrapper)
// ═════════════════════════════════════════════════════════════════════════════

public class PewoJobDataService : IPewoJobDataService
{
    private readonly IPewoDataServiceClient      _client;
    private readonly ILogger<PewoJobDataService> _logger;

    public PewoJobDataService(IPewoDataServiceClient client, ILogger<PewoJobDataService> logger)
    {
        _client = client;
        _logger = logger;
    }

    public async Task<List<DueJobDto>> GetDueJobsAsync(CancellationToken ct)
    {
        try   { return await _client.GetDueJobsAsync(ct); }
        catch (Exception ex) { _logger.LogError(ex, "[PEWO] GetDueJobsAsync failed"); throw; }
    }

    public async Task<List<RunResumeStepDto>> GetRunResumeAsync(int runId, int typeId, CancellationToken ct)
    {
        try   { return await _client.GetRunResumeAsync(runId, typeId, ct); }
        catch (Exception ex) { _logger.LogError(ex, "[PEWO] GetRunResumeAsync RunId={Id} failed", runId); throw; }
    }

    public async Task<List<WorkflowRunEventDto>> GetWorkflowRunEventsAsync(int runId, CancellationToken ct)
    {
        try   { return await _client.GetWorkflowRunEventsAsync(runId, ct); }
        catch (Exception ex) { _logger.LogError(ex, "[PEWO] GetWorkflowRunEventsAsync RunId={Id} failed", runId); throw; }
    }

    public async Task<List<BatchRunStatusDto>> GetBatchRunStatusAsync(string code, string key, CancellationToken ct)
    {
        try   { return await _client.GetBatchRunStatusAsync(code, key, ct); }
        catch (Exception ex) { _logger.LogError(ex, "[PEWO] GetBatchRunStatusAsync BatchKey={Key} failed", key); throw; }
    }

    public async Task<int> CreateWorkflowRunAsync(int idSchedule, int typeId, short maxRetries, CancellationToken ct)
    {
        try   { return await _client.CreateWorkflowRunAsync(idSchedule, typeId, maxRetries, ct); }
        catch (Exception ex) { _logger.LogError(ex, "[PEWO] CreateWorkflowRunAsync ScheduleId={Id} failed", idSchedule); throw; }
    }

    public async Task<int> CreateWorkflowRunEventAsync(
        int runId, int? idEvent, int? idCustomer, int? idStore,
        string? storeNo, string? storeName, Guid? eventGuid, string? eventStatus,
        DateTime? eventScheduledDate, DateTime? eventDate, string? metadataJson, CancellationToken ct)
    {
        try
        {
            return await _client.CreateWorkflowRunEventAsync(
                runId, idEvent, idCustomer, idStore, storeNo, storeName,
                eventGuid, eventStatus, eventScheduledDate, eventDate, metadataJson, ct);
        }
        catch (Exception ex) { _logger.LogError(ex, "[PEWO] CreateWorkflowRunEventAsync RunId={Id} failed", runId); throw; }
    }

    public async Task UpsertStepRunAsync(
        int runId, int stepDefId, string stepKind, string status, short attempts,
        string? artifactRef, string? failureDetails, DateTime? startTime, DateTime? endTime, CancellationToken ct)
    {
        try
        {
            await _client.UpsertStepRunAsync(runId, stepDefId, stepKind, status, attempts,
                artifactRef, failureDetails, startTime, endTime, ct);
        }
        catch (Exception ex) { _logger.LogError(ex, "[PEWO] UpsertStepRunAsync RunId={Id} Step={Kind} failed", runId, stepKind); throw; }
    }

    public async Task SetRunTerminalStatusAsync(
        int runId, string status, string? reason, DateTime? retryAt, short retryCount, CancellationToken ct)
    {
        try   { await _client.SetRunTerminalStatusAsync(runId, status, reason, retryAt, retryCount, ct); }
        catch (Exception ex) { _logger.LogError(ex, "[PEWO] SetRunTerminalStatusAsync RunId={Id} failed", runId); throw; }
    }

    public async Task AdvanceScheduleAsync(int idSchedule, DateTime nextRunAt, int lastRunId, string lastStatus, CancellationToken ct)
    {
        try   { await _client.AdvanceScheduleAsync(idSchedule, nextRunAt, lastRunId, lastStatus, ct); }
        catch (Exception ex) { _logger.LogError(ex, "[PEWO] AdvanceScheduleAsync ScheduleId={Id} failed", idSchedule); throw; }
    }

    public async Task ResetRunForRetryAsync(int runId, CancellationToken ct)
    {
        try   { await _client.ResetRunForRetryAsync(runId, ct); }
        catch (Exception ex) { _logger.LogError(ex, "[PEWO] ResetRunForRetryAsync RunId={Id} failed", runId); throw; }
    }

    public async Task<List<PewoEventCloseResponse>> CreateRunOnEventCloseAsync(
        int idCustomer, int idEvent, string storeNo, string? storeName, DateTime eventDate,
        string? workflowTypeCode, Guid? eventGuid, int? idStore,
        string? eventStatus, DateTime? eventScheduledDate, CancellationToken ct)
    {
        try
        {
            var dtos = await _client.CreateRunOnEventCloseAsync(
                idCustomer, idEvent, storeNo, storeName, eventDate, workflowTypeCode,
                eventGuid, idStore, eventStatus, eventScheduledDate, ct);
            return dtos.Select(d => new PewoEventCloseResponse
            {
                Id_WorkflowRun          = d.Id_WorkflowRun,
                Id_CustomerWorkflowType = d.Id_CustomerWorkflowType,
                WorkflowType_Code       = d.WorkflowType_Code
            }).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[PEWO] CreateRunOnEventCloseAsync Customer={C} Event={E} failed", idCustomer, idEvent);
            throw;
        }
    }
}

// ═════════════════════════════════════════════════════════════════════════════
// PewoLogService — DB log writer; swallows exceptions so log failures
//                  never crash the worker loop
// ═════════════════════════════════════════════════════════════════════════════

public class PewoLogService : IPewoLogService
{
    private readonly IPewoDataServiceClient  _client;
    private readonly ILogger<PewoLogService> _logger;

    public PewoLogService(IPewoDataServiceClient client, ILogger<PewoLogService> logger)
    {
        _client = client;
        _logger = logger;
    }

    public async Task LogAsync(int runId, int? idCustomer, string? customerName, string? stepKind,
        string logLevel, string message, string? eventContext, CancellationToken ct)
    {
        try
        {
            await _client.InsertLogAsync(runId, idCustomer, customerName, stepKind,
                logLevel, message, eventContext, ct);
        }
        catch (Exception ex)
        {
            // Intentionally swallowed — log failure must not interrupt the worker
            _logger.LogError(ex, "[PEWO] PewoLogService.LogAsync failed RunId={RunId} — row not persisted", runId);
        }
    }
}
