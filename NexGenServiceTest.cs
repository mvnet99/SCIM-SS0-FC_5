using Azure.Storage.Blobs;
using Domain.ApiModels;
using Domain.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Tests.System.Services
{
    /// <summary>
    /// Unit tests for NexGenService.
    /// Covers validation logic that does not require a live Blob connection.
    /// Integration tests (real Blob) should run against a dev storage account.
    /// </summary>
    public class NexGenServiceTest
    {
        private readonly Mock<IConfiguration> _mockConfig;
        private readonly Mock<ILogger<NexGenService>> _mockLogger;
        private readonly NexGenService _service;

        public NexGenServiceTest()
        {
            _mockConfig = new Mock<IConfiguration>();
            _mockConfig.Setup(c => c["BlobConnectionString"]).Returns("UseDevelopmentStorage=true");
            _mockLogger = new Mock<ILogger<NexGenService>>();
            _service = new NexGenService(_mockConfig.Object, _mockLogger.Object);
        }

        [Fact]
        public async Task PostNexGenFilesAsync_MissingSourceContainer_ReturnsFalseWithError()
        {
            var request = BuildValidRequest();
            request.SourceContainer = "";

            var result = await _service.PostNexGenFilesAsync(request, CancellationToken.None);

            Assert.False(result.Success);
            Assert.Contains(result.Errors, e => e.Contains("SourceContainer"));
        }

        [Fact]
        public async Task PostNexGenFilesAsync_MissingDestinationContainer_ReturnsFalseWithError()
        {
            var request = BuildValidRequest();
            request.DestinationContainer = "";

            var result = await _service.PostNexGenFilesAsync(request, CancellationToken.None);

            Assert.False(result.Success);
            Assert.Contains(result.Errors, e => e.Contains("DestinationContainer"));
        }

        [Fact]
        public async Task PostNexGenFilesAsync_InvalidFileType_ReturnsFalseWithError()
        {
            var request = BuildValidRequest();
            request.FileType = "INVALID";

            var result = await _service.PostNexGenFilesAsync(request, CancellationToken.None);

            Assert.False(result.Success);
            Assert.Contains(result.Errors, e => e.Contains("FileType"));
        }

        [Theory]
        [InlineData("12")]       // too short
        [InlineData("12345")]    // too long
        [InlineData("AB")]       // too short
        public async Task PostNexGenFilesAsync_StoreNumberNotFourChars_ReturnsFalseWithError(string storeNumber)
        {
            var request = BuildValidRequest();
            request.StoreNumber = storeNumber;

            var result = await _service.PostNexGenFilesAsync(request, CancellationToken.None);

            Assert.False(result.Success);
            Assert.Contains(result.Errors, e => e.Contains("4 characters"));
        }

        [Theory]
        [InlineData("ALL")]
        [InlineData("all")]
        [InlineData("All")]
        public async Task PostNexGenFilesAsync_StoreNumberAll_PassesValidation(string storeNumber)
        {
            // This will fail at container existence check (no real Blob) — but validation should pass
            var request = BuildValidRequest();
            request.StoreNumber = storeNumber;

            var result = await _service.PostNexGenFilesAsync(request, CancellationToken.None);

            // Validation errors only reference StoreNumber format — should be empty
            Assert.DoesNotContain(result.Errors, e => e.Contains("4 characters"));
        }

        [Theory]
        [InlineData("GM")]
        [InlineData("PRPC")]
        [InlineData("BOTH")]
        [InlineData("gm")]
        [InlineData("prpc")]
        [InlineData("both")]
        public async Task PostNexGenFilesAsync_ValidFileTypes_PassValidation(string fileType)
        {
            var request = BuildValidRequest();
            request.FileType = fileType;

            var result = await _service.PostNexGenFilesAsync(request, CancellationToken.None);

            Assert.DoesNotContain(result.Errors, e => e.Contains("FileType"));
        }

        [Fact]
        public async Task PostNexGenFilesAsync_MissingClientCode_ReturnsFalseWithError()
        {
            var request = BuildValidRequest();
            request.ClientCode = "";

            var result = await _service.PostNexGenFilesAsync(request, CancellationToken.None);

            Assert.False(result.Success);
            Assert.Contains(result.Errors, e => e.Contains("ClientCode"));
        }

        [Fact]
        public async Task PostNexGenFilesAsync_CorrelationIdMissing_GeneratesNewGuid()
        {
            var request = BuildValidRequest();
            request.CorrelationId = null;

            var result = await _service.PostNexGenFilesAsync(request, CancellationToken.None);

            // CorrelationId should be populated even when not provided
            Assert.NotNull(result.CorrelationId);
            Assert.NotEmpty(result.CorrelationId);
        }

        [Fact]
        public async Task PostNexGenFilesAsync_DryRunTrue_EchoedInResponse()
        {
            var request = BuildValidRequest();
            request.DryRun = true;

            var result = await _service.PostNexGenFilesAsync(request, CancellationToken.None);

            Assert.True(result.DryRun);
        }

        // -----------------------------------------------------------------------------------------
        // Helpers
        // -----------------------------------------------------------------------------------------

        private NexGenPostFilesRequest BuildValidRequest() => new NexGenPostFilesRequest
        {
            SourceContainer = "fc-hold-target",
            DestinationContainer = "nexgen-delivery",
            ArchiveContainer = "flexcount-save",
            FileType = "BOTH",
            StoreNumber = "ALL",
            ClientCode = "TARGET",
            CorrelationId = "test-correlation-123",
            DryRun = false
        };
    }
}
