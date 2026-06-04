namespace WS.FC.Dapper.Shared.DTOs.Pewo
{
    // ── Due Jobs ─────────────────────────────────────────────────────────────

    public class DueJobDto
    {
        public int ScheduleId { get; set; }
        public int WorkflowTypeId { get; set; }
        public string ScheduleName { get; set; } = string.Empty;
        public string CronExpression { get; set; } = string.Empty;
        public string Timezone { get; set; } = "UTC";
        public DateTime? NextRunAt { get; set; }
        public string Status { get; set; } = string.Empty;
        public Guid? LastRunId { get; set; }
        public string WorkflowCode { get; set; } = string.Empty;
        public string WorkflowName { get; set; } = string.Empty;
        public int MaxRetries { get; set; }
        /// <summary>Null for SCHEDULE source; populated for RETRY source.</summary>
        public Guid? RunId { get; set; }
        /// <summary>'SCHEDULE' or 'RETRY'</summary>
        public string JobSource { get; set; } = "SCHEDULE";
    }

    // ── Run Resume ───────────────────────────────────────────────────────────

    public class RunResumeStepDto
    {
        public int StepDefId { get; set; }
        public int StepOrder { get; set; }
        public string StepKind { get; set; } = string.Empty;
        public string StepName { get; set; } = string.Empty;
        public string? Config { get; set; }
        public int MaxAttempts { get; set; }
        public int BackoffSeconds { get; set; }
        // From StepRun (NULL if not yet run)
        public Guid? StepRunId { get; set; }
        public string? Status { get; set; }
        public int Attempts { get; set; }
        public string? Reason { get; set; }
        public string? ArtifactRef { get; set; }
    }

    // ── Lock ─────────────────────────────────────────────────────────────────

    public class AcquireLockRequestDto
    {
        public int ScheduleId { get; set; }
        public string WorkerId { get; set; } = string.Empty;
    }

    public class ReleaseLockRequestDto
    {
        public int ScheduleId { get; set; }
        public string WorkerId { get; set; } = string.Empty;
        public DateTime NextRunAt { get; set; }
        public Guid LastRunId { get; set; }
        public string LastStatus { get; set; } = string.Empty;
    }

    public class LockResultDto
    {
        public bool Acquired { get; set; }
    }

    // ── Step Upsert ──────────────────────────────────────────────────────────

    public class UpsertStepRunRequestDto
    {
        public Guid RunId { get; set; }
        public int StepDefId { get; set; }
        public string Status { get; set; } = string.Empty;
        public int Attempts { get; set; }
        public string? Reason { get; set; }
        public string? ArtifactRef { get; set; }
        public DateTime? StartedAt { get; set; }
        public DateTime? FinishedAt { get; set; }
    }

    // ── Run Terminal Status ───────────────────────────────────────────────────

    public class SetRunStatusRequestDto
    {
        public Guid RunId { get; set; }
        public string Status { get; set; } = string.Empty;
        public string? Reason { get; set; }
        public DateTime? RetryAt { get; set; }
        public int RetryCount { get; set; }
    }

    // ── Create WorkflowRun ───────────────────────────────────────────────────

    public class CreateWorkflowRunRequestDto
    {
        public Guid RunId { get; set; }
        public int WorkflowTypeId { get; set; }
        public int ScheduleId { get; set; }
        public int MaxRetries { get; set; }
    }
}
