using System;
using System.Collections.Generic;

namespace WS.FC.Dapper.Shared.DTOs.Pewo;

public class DueJobDto
{
    public int Id_Schedule { get; set; }
    public int Id_CustomerWorkflowType { get; set; }
    public string Schedule_Name { get; set; } = string.Empty;
    public string Cron_Expression { get; set; } = string.Empty;
    public string Timezone { get; set; } = "UTC";
    public DateTime? Next_Run_At { get; set; }
    public string Status { get; set; } = string.Empty;
    public int? Last_Run_Id { get; set; }
    public string WorkflowType_Code { get; set; } = string.Empty;
    public string WorkflowType_Name { get; set; } = string.Empty;
    public short Max_Retries { get; set; }
    public int? Id_WorkflowRun { get; set; }
    public string Job_Source { get; set; } = string.Empty;
}

public class RunResumeStepDto
{
    public int Id_WorkflowStepDef { get; set; }
    public int Id_CustomerWorkflowType { get; set; }
    public short Step_Order { get; set; }
    public string Step_Kind { get; set; } = string.Empty;
    public string Step_Name { get; set; } = string.Empty;
    public string? Config { get; set; }
    public short Max_Attempts { get; set; }
    public int Backoff_Seconds { get; set; }
    public int? Id_WorkflowStepRun { get; set; }
    public string? Status { get; set; }
    public short Attempts { get; set; }
    public string? Artifact_Ref { get; set; }
    public string? Failure_Details { get; set; }
}

public class WorkflowRunEventDto
{
    public int Id_WorkflowRunEvent { get; set; }
    public int Id_WorkflowRun { get; set; }
    public int? Id_Event { get; set; }
    public int? Id_Customer { get; set; }
    public int? Id_Store { get; set; }
    public string? Store_No { get; set; }
    public string? Store_Name { get; set; }
    public Guid? Event_Guid { get; set; }
    public string? Event_Status { get; set; }
    public DateTime? Event_Scheduled_Date { get; set; }
    public DateTime? Event_Date { get; set; }
    public string? Metadata_Json { get; set; }
}

public class BatchRunStatusDto
{
    public int Id_WorkflowRun { get; set; }
    public string? Batch_Key { get; set; }
    public string? Store_No { get; set; }
    public string? Store_Name { get; set; }
    public DateTime? Event_Date { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? Reason { get; set; }
}

public class EventCloseResponseDto
{
    public int Id_WorkflowRun { get; set; }
    public int Id_CustomerWorkflowType { get; set; }
    public string WorkflowType_Code { get; set; } = string.Empty;
}

// ── Request DTOs used by PewoDataController endpoints ────────────────────────

public class CreateWorkflowRunDto
{
    public int Id_Schedule { get; set; }
    public int Id_CustomerWorkflowType { get; set; }
    public short Max_Retries { get; set; }
}

public class CreateWorkflowRunEventDto
{
    public int Id_WorkflowRun { get; set; }
    public int? Id_Event { get; set; }
    public int? Id_Customer { get; set; }
    public int? Id_Store { get; set; }
    public string? Store_No { get; set; }
    public string? Store_Name { get; set; }
    public Guid? Event_Guid { get; set; }
    public string? Event_Status { get; set; }
    public DateTime? Event_Scheduled_Date { get; set; }
    public DateTime? Event_Date { get; set; }
    public string? Metadata_Json { get; set; }
}

public class UpsertStepRunDto
{
    public int Id_WorkflowRun { get; set; }
    public int Id_WorkflowStepDef { get; set; }
    public string Step_Kind { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public short Attempts { get; set; }
    public string? Artifact_Ref { get; set; }
    public string? Failure_Details { get; set; }
    public DateTime? Start_Time { get; set; }
    public DateTime? End_Time { get; set; }
}

public class SetRunTerminalStatusDto
{
    public string Status { get; set; } = string.Empty;
    public string? Reason { get; set; }
    public DateTime? Retry_At { get; set; }
    public short Retry_Count { get; set; }
}

public class AdvanceScheduleDto
{
    public DateTime Next_Run_At { get; set; }
    public int Last_Run_Id { get; set; }
    public string Last_Status { get; set; } = string.Empty;
}

public class InsertLogDto
{
    public int? Id_Customer { get; set; }
    public string? Customer_Name { get; set; }
    public string? Step_Kind { get; set; }
    public string Log_Level { get; set; } = "INFO";
    public string Message { get; set; } = string.Empty;
    public string? Event_Context { get; set; }
}

public class CreateRunOnEventCloseDto
{
    public int Id_Customer { get; set; }
    public int Id_Event { get; set; }
    public string Store_No { get; set; } = string.Empty;
    public string? Store_Name { get; set; }
    public DateTime Event_Date { get; set; }
    public string? WorkflowType_Code { get; set; }
    public Guid? Event_Guid { get; set; }
    public int? Id_Store { get; set; }
    public string? Event_Status { get; set; }
    public DateTime? Event_Scheduled_Date { get; set; }
}
