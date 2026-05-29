namespace Domain.ApiModels
{
    /// <summary>
    /// Request model for the NexGen post-files endpoint.
    /// Called by Logic Apps to zip and deliver NexGen GM and PRPC files to the delivery Blob container.
    /// </summary>
    public class NexGenPostFilesRequest
    {
        /// <summary>
        /// The Blob container where the source .txt files were placed by the DBA.
        /// Example: "fc-hold-target"
        /// </summary>
        public string SourceContainer { get; set; }

        /// <summary>
        /// File type to process: "GM", "PRPC", or "BOTH".
        /// Maps directly to TAR_ITM_GM and TAR_ITM_PRPC file name prefixes.
        /// </summary>
        public string FileType { get; set; }

        /// <summary>
        /// 4-digit store number to filter files, or "ALL" to process all matching files.
        /// Mirrors the $1 argument in the original Unix scripts.
        /// Example: "0123" or "ALL"
        /// </summary>
        public string StoreNumber { get; set; }

        /// <summary>
        /// Blob container where zipped output files will be written for Logic Apps to pick up.
        /// Example: "nexgen-delivery"
        /// </summary>
        public string DestinationContainer { get; set; }

        /// <summary>
        /// Blob container where backup copies of the zip and original files are written.
        /// Example: "flexcount-save"
        /// </summary>
        public string ArchiveContainer { get; set; }

        /// <summary>
        /// Client identifier. Used for logging and future multi-client support.
        /// Example: "TARGET"
        /// </summary>
        public string ClientCode { get; set; }

        /// <summary>
        /// Correlation ID passed by Logic Apps for end-to-end traceability.
        /// If omitted, a new GUID is generated.
        /// </summary>
        public string CorrelationId { get; set; }

        /// <summary>
        /// When true, validates and logs all steps without writing any files.
        /// Use for UAT and smoke testing.
        /// </summary>
        public bool DryRun { get; set; } = false;
    }
}
