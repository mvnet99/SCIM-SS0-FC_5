using Domain.ApiModels.Pewo;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using WS.FC.Dapper.Shared.DTOs.Pewo;

namespace Domain.Services.Interfaces.Pewo;

// ── Worker orchestration ──────────────────────────────────────────────────────

public interface IPewoWorkerService
{
    Task<PewoWorkerRunResponse> RunAsync(CancellationToken cancellationToken);
}

// ── Step execution ────────────────────────────────────────────────────────────

public interface IPewoStepService
{
    /// <summary>Day 1 — validates GM and PRPC file totals via ITotalsValidationService.ValidateNgen.</summary>
    PewoStepResponse TotalsCheck(PewoStepRequest request);

    /// <summary>MEO — discovers blob files closed since last run. Stub until MEO file location confirmed.</summary>
    Task<PewoStepResponse> GetEventsAsync(PewoStepRequest request, CancellationToken cancellationToken);

    /// <summary>MEO — applies file conversion (EBCDIC for ICNTS, ASCII for CPINV/CTL). Stub for NGen delivery.</summary>
    Task<PewoStepResponse> TransformAsync(PewoStepRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Day 2 — reads each source .txt from output-files/{eventGuid}/, creates one individual zip per file
    /// (same name + .zip), saves each zip to flexcount-save.
    /// Artifact_Ref: staged:TAR_ITM_GM_0421_...zip,TAR_ITM_PRPC_0421_...zip
    /// </summary>
    Task<PewoStepResponse> ReadBlobZipAsync(PewoStepRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Day 2 — delivers each zip from flexcount-save to TARGET SFTP via IFtpHelper.UploadFile.
    /// Reads zip list from prior step artifact_ref (staged: format).
    /// Artifact_Ref: sftp:delivered:{remotePath}:file1.zip,file2.zip
    /// </summary>
    Task<PewoStepResponse> SftpAsync(PewoStepRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Day 2 — copies original .txt source files from output-files/{eventGuid}/ to flexcount-save.
    /// Zips are already in flexcount-save from READ_BLOB_ZIP — nothing extra needed.
    /// No deletion: files are in eventGuid subfolder, unique per event, no reprocessing risk.
    /// Artifact_Ref: archived:{count}:{eventGuid}
    /// </summary>
    Task<PewoStepResponse> ArchiveAsync(PewoStepRequest request, CancellationToken cancellationToken);

    /// <summary>Per-event/run notification email via SendGrid.</summary>
    Task<PewoStepResponse> EmailAsync(PewoStepRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Consolidated batch ack email for GM_PRC_DELIVERY fan-out.
    /// Returns failure while any run in the batch is PENDING/RUNNING — retry acts as wait gate.
    /// </summary>
    Task<PewoStepResponse> EmailSummaryAsync(PewoStepRequest request, CancellationToken cancellationToken);
}

// ── Data access ───────────────────────────────────────────────────────────────

public interface IPewoJobDataService
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
    Task<List<PewoEventCloseResponse>> CreateRunOnEventCloseAsync(int idCustomer, int idEvent, string storeNo, string? storeName, DateTime eventDate, string? workflowTypeCode, Guid? eventGuid, int? idStore, string? eventStatus, DateTime? eventScheduledDate, CancellationToken cancellationToken);
}

// ── Logging ───────────────────────────────────────────────────────────────────

public interface IPewoLogService
{
    /// <summary>
    /// Writes a row to Pewo_WorkflowRunLog.
    /// Swallows exceptions — log failures must never crash the worker.
    /// </summary>
    Task LogAsync(int idWorkflowRun, int? idCustomer, string? customerName, string? stepKind,
        string logLevel, string message, string? eventContext, CancellationToken cancellationToken);
}
