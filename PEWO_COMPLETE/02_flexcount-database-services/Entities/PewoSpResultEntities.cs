using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace WS.FC.Dapper.Domain.Entities;

/// <summary>
/// Typed result entity for usp_Pewo_GetDueJobs.
/// Properties match SP output column names exactly so Dapper maps without dynamic casting.
/// Used only via CustomQueryAsync — never for CRUD operations.
/// </summary>
[Table("Pewo_Schedule")]
public class DueJobResult
{
    [Key]
    public int id_Schedule { get; set; }
    public int id_CustomerWorkflowType { get; set; }
    public string Schedule_Name { get; set; } = string.Empty;
    public string Cron_Expression { get; set; } = string.Empty;
    public string Timezone { get; set; } = "UTC";
    public DateTime? Next_Run_At { get; set; }
    public string Status { get; set; } = string.Empty;
    public int? Last_Run_Id { get; set; }
    public string WorkflowType_Code { get; set; } = string.Empty;
    public string WorkflowType_Name { get; set; } = string.Empty;
    public short Max_Retries { get; set; }
    public int? id_WorkflowRun { get; set; }
    public string Job_Source { get; set; } = string.Empty;
}

/// <summary>
/// Typed result entity for usp_Pewo_GetRunResume.
/// LEFT JOIN of WorkflowStepDef to WorkflowStepRun — nullable fields come from the step run side.
/// </summary>
[Table("Pewo_WorkflowStepDef")]
public class RunResumeStepResult
{
    [Key]
    public int id_WorkflowStepDef { get; set; }
    public int id_CustomerWorkflowType { get; set; }
    public short Step_Order { get; set; }
    public string Step_Kind { get; set; } = string.Empty;
    public string Step_Name { get; set; } = string.Empty;
    public string? Config { get; set; }
    public short Max_Attempts { get; set; }
    public int Backoff_Seconds { get; set; }
    public int? id_WorkflowStepRun { get; set; }
    public string? Status { get; set; }
    public short? Attempts { get; set; }
    public string? Artifact_Ref { get; set; }
    public string? Failure_Details { get; set; }
}

/// <summary>
/// Typed result entity for usp_Pewo_GetBatchRunStatus.
/// Returns per-event delivery status for a GM_PRC_DELIVERY batch.
/// </summary>
[Table("Pewo_WorkflowRun")]
public class BatchRunStatusResult
{
    [Key]
    public int id_WorkflowRun { get; set; }
    public string? Batch_Key { get; set; }
    public string? Store_No { get; set; }
    public string? Store_Name { get; set; }
    public DateTime? Event_Date { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? Reason { get; set; }
}

/// <summary>
/// Typed result entity for usp_Pewo_CreateRunOnEventClose.
/// Returns one row per workflow run created.
/// </summary>
[Table("Pewo_WorkflowRun")]
public class EventCloseResult
{
    [Key]
    public int id_WorkflowRun { get; set; }
    public int id_CustomerWorkflowType { get; set; }
    public string WorkflowType_Code { get; set; } = string.Empty;
}
