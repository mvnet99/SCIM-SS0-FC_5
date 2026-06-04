using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace WS.FC.Dapper.Domain.Entities
{
    [Table("WorkflowType", Schema = "workflow")]
    public class WorkflowType
    {
        [Key]
        public int workflow_type_id { get; set; }
        public int customer_id { get; set; }
        public string workflow_code { get; set; } = string.Empty;
        public string workflow_name { get; set; } = string.Empty;
        public string? description { get; set; }
        public int max_retries { get; set; }
        public bool is_active { get; set; }
        public DateTime created_at { get; set; }
        public DateTime updated_at { get; set; }
    }

    [Table("WorkflowStepDef", Schema = "workflow")]
    public class WorkflowStepDef
    {
        [Key]
        public int step_def_id { get; set; }
        public int workflow_type_id { get; set; }
        public int step_order { get; set; }
        public string step_kind { get; set; } = string.Empty;
        public string step_name { get; set; } = string.Empty;
        public string? config { get; set; }
        public int max_attempts { get; set; }
        public int backoff_seconds { get; set; }
        public bool is_active { get; set; }
        public DateTime created_at { get; set; }
        public DateTime updated_at { get; set; }
    }

    [Table("Schedule", Schema = "workflow")]
    public class PewoSchedule
    {
        [Key]
        public int schedule_id { get; set; }
        public int workflow_type_id { get; set; }
        public string schedule_name { get; set; } = string.Empty;
        public string cron_expression { get; set; } = string.Empty;
        public string timezone { get; set; } = "UTC";
        public DateTime? next_run_at { get; set; }
        public DateTime? last_run_at { get; set; }
        public string status { get; set; } = "ACTIVE";
        public string? locked_by { get; set; }
        public DateTime? locked_at { get; set; }
        public string? last_status { get; set; }
        public Guid? last_run_id { get; set; }
        public string? last_error { get; set; }
        public bool is_enabled { get; set; }
        public DateTime created_at { get; set; }
        public DateTime updated_at { get; set; }
    }

    [Table("WorkflowRun", Schema = "workflow")]
    public class WorkflowRun
    {
        [Key]
        public Guid run_id { get; set; }
        public int workflow_type_id { get; set; }
        public int schedule_id { get; set; }
        public string status { get; set; } = "PENDING";
        public string? reason { get; set; }
        public string? locked_by { get; set; }
        public DateTime? locked_at { get; set; }
        public DateTime? retry_at { get; set; }
        public int retry_count { get; set; }
        public int max_retries { get; set; }
        public DateTime created_at { get; set; }
        public DateTime? started_at { get; set; }
        public DateTime? finished_at { get; set; }
    }

    [Table("JobEvent", Schema = "workflow")]
    public class JobEvent
    {
        [Key]
        public int job_event_id { get; set; }
        public Guid run_id { get; set; }
        public int event_id { get; set; }
        public string store_no { get; set; } = string.Empty;
        public string? store_name { get; set; }
        public DateTime? event_date { get; set; }
        public string? file_pattern_gm { get; set; }
        public string? file_pattern_prpc { get; set; }
        public string? metadata_json { get; set; }
        public DateTime created_at { get; set; }
    }

    [Table("StepRun", Schema = "workflow")]
    public class StepRun
    {
        [Key]
        public Guid step_run_id { get; set; }
        public Guid run_id { get; set; }
        public int step_def_id { get; set; }
        public string status { get; set; } = "PENDING";
        public int attempts { get; set; }
        public string? reason { get; set; }
        public string? artifact_ref { get; set; }
        public DateTime? started_at { get; set; }
        public DateTime? finished_at { get; set; }
        public DateTime created_at { get; set; }
        public DateTime updated_at { get; set; }
    }
}
