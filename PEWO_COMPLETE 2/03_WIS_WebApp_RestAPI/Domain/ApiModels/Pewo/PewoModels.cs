using System;

namespace Domain.ApiModels.Pewo;

/// <summary>
/// Request passed from PewoWorkerService into every step method.
/// Populated once per run from WorkflowRunEvent and WorkflowStepDef rows.
/// </summary>
public class PewoStepRequest
{
    public int     Id_WorkflowRun     { get; set; }
    public int     Id_WorkflowStepDef { get; set; }
    public string  Step_Kind          { get; set; } = string.Empty;
    public string? Config             { get; set; }
    public string? Artifact_Ref       { get; set; }
    public short   Attempts           { get; set; }
    public short   Max_Attempts       { get; set; }

    /// <summary>
    /// Event_Guid from WorkflowRunEvent.
    /// Primary blob lookup key — source files live in output-files/{eventGuid}/
    /// </summary>
    public string? Event_Guid  { get; set; }

    /// <summary>Store_No from WorkflowRunEvent — used for log context.</summary>
    public string? Store_No    { get; set; }

    /// <summary>Event_Date from WorkflowRunEvent — used for MEO T{MMDDYY} file naming.</summary>
    public DateTime? Event_Date { get; set; }
}

/// <summary>
/// Result returned by every step method.
/// Success=true  → worker advances to next step.
/// Success=false → worker stops this run, applies retry/backoff.
/// </summary>
public class PewoStepResponse
{
    public bool    Success         { get; set; }
    public string? Artifact_Ref    { get; set; }
    public string? Failure_Details { get; set; }
}

/// <summary>Response from POST /api/Pewo/worker/run</summary>
public class PewoWorkerRunResponse
{
    public int    JobsProcessed { get; set; }
    public int    JobsSucceeded { get; set; }
    public int    JobsFailed    { get; set; }
    public string DurationMs    { get; set; } = string.Empty;
}

/// <summary>Request body for POST /api/Pewo/event-close</summary>
public class PewoEventCloseRequest
{
    public int       Id_Customer          { get; set; }
    public int       Id_Event             { get; set; }
    public string    Store_No             { get; set; } = string.Empty;
    public string?   Store_Name           { get; set; }
    public DateTime  Event_Date           { get; set; }
    /// <summary>Null = prime all active workflows for this customer.</summary>
    public string?   WorkflowType_Code    { get; set; }
    public Guid?     Event_Guid           { get; set; }
    public int?      Id_Store             { get; set; }
    public string?   Event_Status         { get; set; }
    public DateTime? Event_Scheduled_Date { get; set; }
}

/// <summary>One row per WorkflowRun created by event-close.</summary>
public class PewoEventCloseResponse
{
    public int    Id_WorkflowRun          { get; set; }
    public int    Id_CustomerWorkflowType { get; set; }
    public string WorkflowType_Code       { get; set; } = string.Empty;
}
