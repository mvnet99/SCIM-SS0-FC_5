namespace Domain.ApiModels.Pewo
{
    // ── Worker trigger ────────────────────────────────────────────────────────

    public class WorkerRunResponse
    {
        public int JobsProcessed { get; set; }
        public int JobsSucceeded { get; set; }
        public int JobsFailed { get; set; }
        public string DurationMs { get; set; } = string.Empty;
    }

    // ── Totals check ──────────────────────────────────────────────────────────

    public class TotalsCheckRequest
    {
        /// <summary>RunId for logging / traceability. Optional.</summary>
        public Guid? RunId { get; set; }
        /// <summary>Store number e.g. "0421".</summary>
        public string StoreNo { get; set; } = string.Empty;
        public string FilePatternGm { get; set; } = string.Empty;
        public string FilePatternPrpc { get; set; } = string.Empty;
        public string SourceContainer { get; set; } = "fc-hold-target";
    }

    public class TotalsCheckResponse
    {
        public bool Passed { get; set; }
        public string Reason { get; set; } = string.Empty;
        public int FilesChecked { get; set; }
    }

    // ── Health ────────────────────────────────────────────────────────────────

    public class PewoHealthResponse
    {
        public string Status { get; set; } = "ok";
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }
}
