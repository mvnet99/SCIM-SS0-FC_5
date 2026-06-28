using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace WS.FC.Dapper.Domain.Entities;

[Table("Pewo_CustomerWorkflowType")]
public class CustomerWorkflowType
{
    [Key]
    public int id_CustomerWorkflowType { get; set; }
    public int id_Customer { get; set; }
    public string WorkflowType_Code { get; set; } = string.Empty;
    public string WorkflowType_Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public short Max_Retries { get; set; }
    public bool is_Active { get; set; }
    public string? Fan_Out_Source_WorkflowType_Code { get; set; }
    public int? Fan_Out_Lookback_Hours { get; set; }
    public DateTime? created_date { get; set; }
    public DateTime? last_updated_date { get; set; }
    public int? created_by { get; set; }
    public int? updated_by { get; set; }
}

[Table("Pewo_StepKind")]
public class PewoStepKind
{
    [Key]
    public string Step_Kind { get; set; } = string.Empty;
}

[Table("Pewo_WorkflowStepDef")]
public class WorkflowStepDef
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
    public bool is_Active { get; set; }
    public DateTime? created_date { get; set; }
    public DateTime? last_updated_date { get; set; }
    public int? created_by { get; set; }
    public int? updated_by { get; set; }
}

[Table("Pewo_Schedule")]
public class PewoSchedule
{
    [Key]
    public int id_Schedule { get; set; }
    public int id_CustomerWorkflowType { get; set; }
    public string Schedule_Name { get; set; } = string.Empty;
    public string Cron_Expression { get; set; } = string.Empty;
    public string Timezone { get; set; } = "UTC";
    public DateTime? Next_Run_At { get; set; }
    public DateTime? Last_Run_At { get; set; }
    public string Status { get; set; } = "ACTIVE";
    public string? Last_Status { get; set; }
    public int? Last_Run_Id { get; set; }
    public bool is_Enabled { get; set; }
    public DateTime? created_date { get; set; }
    public DateTime? last_updated_date { get; set; }
    public int? created_by { get; set; }
    public int? updated_by { get; set; }
}

[Table("Pewo_WorkflowRun")]
public class WorkflowRun
{
    [Key]
    public int id_WorkflowRun { get; set; }
    public int id_Schedule { get; set; }
    public int id_CustomerWorkflowType { get; set; }
    public string Status { get; set; } = "PENDING";
    public string? Reason { get; set; }
    public DateTime? Retry_At { get; set; }
    public short Retry_Count { get; set; }
    public short Max_Retries { get; set; }
    public string? Batch_Key { get; set; }
    public DateTime? Started_At { get; set; }
    public DateTime? Finished_At { get; set; }
    public DateTime? created_date { get; set; }
    public DateTime? last_updated_date { get; set; }
    public int? created_by { get; set; }
    public int? updated_by { get; set; }
}

[Table("Pewo_WorkflowRunEvent")]
public class WorkflowRunEvent
{
    [Key]
    public int id_WorkflowRunEvent { get; set; }
    public int id_WorkflowRun { get; set; }
    public int? id_Event { get; set; }
    public int? id_Customer { get; set; }
    public int? id_Store { get; set; }
    public string? Store_No { get; set; }
    public string? Store_Name { get; set; }
    public Guid? Event_Guid { get; set; }
    public string? Event_Status { get; set; }
    public DateTime? Event_Scheduled_Date { get; set; }
    public DateTime? Event_Date { get; set; }
    public string? Metadata_Json { get; set; }
    public DateTime? created_date { get; set; }
    public int? created_by { get; set; }
}

[Table("Pewo_WorkflowStepRun")]
public class WorkflowStepRun
{
    [Key]
    public int id_WorkflowStepRun { get; set; }
    public int id_WorkflowRun { get; set; }
    public int id_WorkflowStepDef { get; set; }
    public string Step_Kind { get; set; } = string.Empty;
    public string Status { get; set; } = "PENDING";
    public short Attempts { get; set; }
    public string? Artifact_Ref { get; set; }
    public string? Failure_Details { get; set; }
    public DateTime? Start_Time { get; set; }
    public DateTime? End_Time { get; set; }
    public DateTime? created_date { get; set; }
    public DateTime? last_updated_date { get; set; }
    public int? created_by { get; set; }
    public int? updated_by { get; set; }
}

[Table("Pewo_WorkflowRunLog")]
public class WorkflowRunLog
{
    [Key]
    public int id_WorkflowRunLog { get; set; }
    public int id_WorkflowRun { get; set; }
    public int? id_Customer { get; set; }
    public string? Customer_Name { get; set; }
    public string? Step_Kind { get; set; }
    public string Log_Level { get; set; } = "INFO";
    public string Message { get; set; } = string.Empty;
    public string? Event_Context { get; set; }
    public DateTime logged_date { get; set; }
    public int? created_by { get; set; }
}
