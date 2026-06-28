using Domain.ApiModels;
using Domain.ApiModels.Pewo;
using Domain.Constants;
using Domain.Helpers.Interfaces;
using Domain.Services;
using Domain.Services.Interfaces;
using Domain.Services.Interfaces.Pewo;
using Domain.Services.Pewo;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using WS.FC.Dapper.Shared.DTOs.Pewo;
using Xunit;

namespace Tests.System.Services;

public class PewoStepServiceTest
{
    // Note: PewoStepService uses BlobContainerClient inline for blob listing
    // (same pattern as TotalsValidationService). Tests for blob listing operations
    // require an integration test or mock of BlobContainerClient.
    // Unit tests below focus on the injectable dependencies and business logic.

    public readonly Mock<ITotalsValidationService>   _totalsService      = new();
    public readonly Mock<IAzureBlobStorageHelper>    _blobHelper         = new();
    public readonly Mock<IFtpHelper>                 _ftpHelper          = new();
    public readonly Mock<IPewoJobDataService>        _jobDataService     = new();
    public readonly Mock<IConfiguration>             _configuration      = new();
    public readonly Mock<ILogger<PewoStepService>>   _logger             = new();

    private PewoStepService BuildService()
    {
        // Set up blob connection string to prevent NullReferenceException in constructor
        _configuration.Setup(c => c[WISAppConstants.VaultBlobKey]).Returns("UseDevelopmentStorage=true");
        return new PewoStepService(
            _totalsService.Object, _blobHelper.Object, _ftpHelper.Object,
            _jobDataService.Object, _configuration.Object, _logger.Object);
    }

    private static PewoStepRequest BuildRequest(string kind, string? config = null, string? artifactRef = null) =>
        new()
        {
            Id_WorkflowRun = 1, Id_WorkflowStepDef = 1, Step_Kind = kind,
            Config = config, Artifact_Ref = artifactRef,
            Attempts = 1, Max_Attempts = 3,
            Event_Guid = Guid.NewGuid().ToString(),
            Store_No = "T1234", Event_Date = DateTime.UtcNow.AddDays(-1)
        };

    // ── TOTALS_CHECK ──────────────────────────────────────────────────────────

    [Fact]
    public void TotalsCheck_ReturnsSuccess_WhenValidationPasses()
    {
        var svc = BuildService();
        _totalsService.Setup(s => s.ValidateNgen(It.IsAny<string>()))
            .Returns(new TotalsValidationResult
            {
                Success = true, TotalQty = 1000, TotalExt = 5000,
                Messages = new List<TotalsValidationMessage>()
            });

        var result = svc.TotalsCheck(BuildRequest("TOTALS_CHECK"));

        result.Success.Should().BeTrue();
        result.Artifact_Ref.Should().Contain("totalQty");
    }

    [Fact]
    public void TotalsCheck_ReturnsFail_WhenValidationFails()
    {
        var svc = BuildService();
        _totalsService.Setup(s => s.ValidateNgen(It.IsAny<string>()))
            .Returns(new TotalsValidationResult
            {
                Success = false,
                Messages = new List<TotalsValidationMessage>
                {
                    new() { Step = "Header mismatch", Status = "Error" }
                }
            });

        var result = svc.TotalsCheck(BuildRequest("TOTALS_CHECK"));

        result.Success.Should().BeFalse();
        result.Failure_Details.Should().Contain("Header mismatch");
    }

    [Fact]
    public void TotalsCheck_ReturnsFail_WhenEventGuidMissing()
    {
        var svc     = BuildService();
        var request = BuildRequest("TOTALS_CHECK");
        request.Event_Guid = null;

        var result = svc.TotalsCheck(request);

        result.Success.Should().BeFalse();
        result.Failure_Details.Should().Contain("Event_Guid");
        _totalsService.Verify(s => s.ValidateNgen(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public void TotalsCheck_ReturnsFail_WhenValidationServiceThrows()
    {
        var svc = BuildService();
        _totalsService.Setup(s => s.ValidateNgen(It.IsAny<string>()))
            .Throws(new Exception("Service unavailable"));

        var result = svc.TotalsCheck(BuildRequest("TOTALS_CHECK"));

        result.Success.Should().BeFalse();
        result.Failure_Details.Should().Contain("Exception");
    }

    // ── SFTP ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SftpAsync_SkipsDelivery_WhenAlreadyDelivered()
    {
        var svc     = BuildService();
        var request = BuildRequest("SFTP", artifactRef: "sftp:delivered:/www.data/target-ssh/nexgen:file.zip");

        var result = await svc.SftpAsync(request, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Artifact_Ref.Should().StartWith("sftp:delivered:");
        _ftpHelper.Verify(f => f.UploadFile(It.IsAny<FileStream>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<ILogger>()), Times.Never);
    }

    [Fact]
    public async Task SftpAsync_ReturnsFail_WhenArtifactRefNotStaged()
    {
        var svc     = BuildService();
        var request = BuildRequest("SFTP", config: "{\"remotePath\":\"/www.data/target-ssh/nexgen\"}",
            artifactRef: "some-wrong-format");

        var result = await svc.SftpAsync(request, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Failure_Details.Should().Contain("staged:");
    }

    [Fact]
    public async Task SftpAsync_ReturnsFail_WhenRemotePathMissing()
    {
        var svc     = BuildService();
        var request = BuildRequest("SFTP", config: "{}", artifactRef: "staged:file.zip");

        var result = await svc.SftpAsync(request, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Failure_Details.Should().Contain("remotePath");
    }

    [Fact]
    public async Task SftpAsync_ReturnsFail_WhenZipNotFoundInStaging()
    {
        var svc     = BuildService();
        var request = BuildRequest("SFTP",
            config: "{\"remotePath\":\"/www.data/target-ssh/nexgen\",\"stagingContainer\":\"flexcount-save\"}",
            artifactRef: "staged:TAR_ITM_GM_0421_20250813_143022.zip");

        _blobHelper.Setup(b => b.DownloadFileBlob(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync((MemoryStream?)null);

        var result = await svc.SftpAsync(request, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Failure_Details.Should().Contain("not found or empty");
    }

    // ── ARCHIVE ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task ArchiveAsync_SkipsArchive_WhenAlreadyArchived()
    {
        var svc     = BuildService();
        var eventGuid = Guid.NewGuid().ToString();
        var request = BuildRequest("ARCHIVE", artifactRef: $"archived:2:{eventGuid}");
        request.Event_Guid = eventGuid;

        var result = await svc.ArchiveAsync(request, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Artifact_Ref.Should().StartWith("archived:");
        _blobHelper.Verify(b => b.UploadFileAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<string>()),
            Times.Never);
    }

    [Fact]
    public async Task ArchiveAsync_ReturnsFail_WhenEventGuidMissing()
    {
        var svc     = BuildService();
        var request = BuildRequest("ARCHIVE");
        request.Event_Guid = null;

        var result = await svc.ArchiveAsync(request, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Failure_Details.Should().Contain("Event_Guid");
    }

    // ── EMAIL ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task EmailAsync_SkipsSend_WhenAlreadyNotified()
    {
        var svc     = BuildService();
        var request = BuildRequest("EMAIL", artifactRef: $"notified:{DateTime.UtcNow:O}");

        var result = await svc.EmailAsync(request, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Artifact_Ref.Should().StartWith("notified:");
    }

    [Fact]
    public async Task EmailAsync_ReturnsFail_WhenNoRecipients()
    {
        var svc     = BuildService();
        var request = BuildRequest("EMAIL", config: "{\"subject\":\"Test\"}");

        var result = await svc.EmailAsync(request, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Failure_Details.Should().Contain("recipients");
    }

    // ── EMAIL_SUMMARY ─────────────────────────────────────────────────────────

    [Fact]
    public async Task EmailSummaryAsync_SkipsSend_WhenAlreadySent()
    {
        var svc     = BuildService();
        var request = BuildRequest("EMAIL_SUMMARY", artifactRef: "summary-sent:2025-01-01:3events");

        var result = await svc.EmailSummaryAsync(request, CancellationToken.None);

        result.Success.Should().BeTrue();
        _jobDataService.Verify(s => s.GetBatchRunStatusAsync(It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task EmailSummaryAsync_ReturnsFail_WhenBatchStillActive()
    {
        var svc     = BuildService();
        var request = BuildRequest("EMAIL_SUMMARY",
            config: "{\"batchWorkflowTypeCode\":\"GM_PRC_DELIVERY\",\"recipients\":\"ops@company.com\"}");
        request.Attempts    = 1;
        request.Max_Attempts = 5;

        _jobDataService.Setup(s => s.GetBatchRunStatusAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<BatchRunStatusDto>
            {
                new() { Id_WorkflowRun = 1, Status = "PENDING",   Store_No = "T001" },
                new() { Id_WorkflowRun = 2, Status = "COMPLETED", Store_No = "T002" }
            });

        var result = await svc.EmailSummaryAsync(request, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Failure_Details.Should().Contain("not fully terminal");
    }

    [Fact]
    public async Task EmailSummaryAsync_ReturnsFail_WhenNoBatchWorkflowTypeCode()
    {
        var svc     = BuildService();
        var request = BuildRequest("EMAIL_SUMMARY", config: "{\"recipients\":\"ops@company.com\"}");

        var result = await svc.EmailSummaryAsync(request, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Failure_Details.Should().Contain("batchWorkflowTypeCode");
    }

    // ── TRANSFORM (stub) ──────────────────────────────────────────────────────

    [Fact]
    public async Task TransformAsync_ReturnsSuccess_AsStub()
    {
        var svc    = BuildService();
        var result = await BuildService().TransformAsync(BuildRequest("TRANSFORM"), CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Artifact_Ref.Should().Contain("transform-stub");
    }

    // ── GET_EVENTS ────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetEventsAsync_SkipsDiscovery_WhenAlreadyDiscovered()
    {
        var svc     = BuildService();
        var request = BuildRequest("GET_EVENTS", artifactRef: "discovered:3:20250101");

        var result = await svc.GetEventsAsync(request, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Artifact_Ref.Should().Be("discovered:3:20250101");
    }
}
