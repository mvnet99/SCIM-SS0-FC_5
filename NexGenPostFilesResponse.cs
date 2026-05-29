using System.Collections.Generic;

namespace Domain.ApiModels
{
    /// <summary>
    /// Response returned by the NexGen post-files endpoint.
    /// </summary>
    public class NexGenPostFilesResponse
    {
        /// <summary>
        /// True if all files were processed successfully.
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// Echoed correlation ID for Logic Apps traceability.
        /// </summary>
        public string CorrelationId { get; set; }

        /// <summary>
        /// Client code from the request.
        /// </summary>
        public string ClientCode { get; set; }

        /// <summary>
        /// Whether this was a dry run (no files written).
        /// </summary>
        public bool DryRun { get; set; }

        /// <summary>
        /// List of source .txt files that were found and processed.
        /// </summary>
        public List<string> FilesProcessed { get; set; } = new();

        /// <summary>
        /// List of zip files written to the destination container.
        /// Logic Apps reads these to SFTP deliver to Target.
        /// </summary>
        public List<string> ZipFilesCreated { get; set; } = new();

        /// <summary>
        /// List of backup files written to the archive container.
        /// </summary>
        public List<string> ArchiveFilesCreated { get; set; } = new();

        /// <summary>
        /// Total count of source files processed.
        /// </summary>
        public int FilesCount { get; set; }

        /// <summary>
        /// Any non-fatal warnings encountered during processing.
        /// </summary>
        public List<string> Warnings { get; set; } = new();

        /// <summary>
        /// Error details if Success is false.
        /// </summary>
        public List<string> Errors { get; set; } = new();
    }
}
