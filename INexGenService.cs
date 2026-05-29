using Domain.ApiModels;
using System.Threading;
using System.Threading.Tasks;

namespace Domain.Services.Interfaces
{
    public interface INexGenService
    {
        /// <summary>
        /// Processes NexGen GM and/or PRPC files from Blob storage:
        /// streams source .txt files, zips in memory, writes zip to destination container,
        /// and writes backup copies to archive container.
        /// Mirrors the logic of post-nexgen-gm-file and post-nexgen-prpc-file Unix scripts.
        /// </summary>
        Task<NexGenPostFilesResponse> PostNexGenFilesAsync(
            NexGenPostFilesRequest request,
            CancellationToken cancellationToken);
    }
}
