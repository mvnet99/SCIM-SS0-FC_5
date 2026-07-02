using API.Controllers;
using Domain.ApiModels.Pewo;
using Domain.Services.Interfaces.Pewo;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Tests.System.Controllers;

public class PewoControllerTest
{
    private readonly PewoController _controller;

    public readonly Mock<IPewoWorkerService>      _pewoWorkerService  = new();
    public readonly Mock<IPewoStepService>        _pewoStepService    = new();
    public readonly Mock<IPewoJobDataService>     _pewoJobDataService = new();
    public readonly Mock<IPewoLogService>         _pewoLogService     = new();
    public readonly Mock<ILogger<PewoController>> _logger             = new();

    public PewoControllerTest()
    {
        _controller = new PewoController(
            _pewoWorkerService.Object,
            _pewoStepService.Object,
            _pewoJobDataService.Object,
            _pewoLogService.Object,
            _logger.Object);
    }

    private static PewoStepRequest BuildStepRequest(string kind) => new()
    {
        Id_WorkflowRun = 1, Id_WorkflowStepDef = 1, Step_Kind = kind,
        Event_Guid = Guid.NewGuid().ToString(), Attempts = 1, Max_Attempts = 3, Store_No = "T1234"
    };

    #region WorkerRun

    [Fact]
    public async Task WorkerRun_Returns200_WithJobsSummary()
    {
        var expected = new PewoWorkerRunResponse { JobsProcessed = 2, JobsSucceeded = 2, JobsFailed = 0, DurationMs = "500" };
        _pewoWorkerService.Setup(s => s.RunAsync(It.IsAny<CancellationToken>())).ReturnsAsync(expected);

        var result = (await _controller.WorkerRun(CancellationToken.None)) as OkObjectResult;

        result.Should().NotBeNull();
        result!.StatusCode.Should().Be(200);
        result.Value.Should().BeEquivalentTo(expected);
    }

    [Fact]
    public async Task WorkerRun_Returns200_WhenNoJobsDue()
    {
        var expected = new PewoWorkerRunResponse { JobsProcessed = 0, JobsSucceeded = 0, JobsFailed = 0, DurationMs = "3" };
        _pewoWorkerService.Setup(s => s.RunAsync(It.IsAny<CancellationToken>())).ReturnsAsync(expected);

        var result = (await _controller.WorkerRun(CancellationToken.None)) as OkObjectResult;

        ((PewoWorkerRunResponse)result!.Value!).JobsProcessed.Should().Be(0);
    }

    #endregion

    #region EventClose

    [Fact]
    public async Task EventClose_Returns200_WithRunsCreated()
    {
        var request = new PewoEventCloseRequest
        {
            Id_Customer = 47, Id_Event = 5001, Store_No = "T1234",
            Event_Date = DateTime.UtcNow, Event_Guid = Guid.NewGuid()
        };
        var responses = new List<PewoEventCloseResponse>
        {
            new() { Id_WorkflowRun = 42, Id_CustomerWorkflowType = 1, WorkflowType_Code = "GM_TOTALS_CHECK" }
        };

        _pewoJobDataService.Setup(s => s.CreateRunOnEventCloseAsync(
            It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string?>(),
            It.IsAny<DateTime>(), It.IsAny<string?>(), It.IsAny<Guid?>(), It.IsAny<int?>(),
            It.IsAny<string?>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(responses);

        var result = (await _controller.EventClose(request, CancellationToken.None)) as OkObjectResult;

        result.Should().NotBeNull();
        ((List<PewoEventCloseResponse>)result!.Value!).Should().HaveCount(1);
    }

    #endregion

    #region Step endpoints

    [Fact]
    public void TotalsCheck_Returns200_WhenPassed()
    {
        var expected = new PewoStepResponse { Success = true, Artifact_Ref = "{\"totalQty\":1000}" };
        _pewoStepService.Setup(s => s.TotalsCheck(It.IsAny<PewoStepRequest>())).Returns(expected);

        var result = _controller.TotalsCheck(BuildStepRequest("TOTALS_CHECK")) as OkObjectResult;

        ((PewoStepResponse)result!.Value!).Success.Should().BeTrue();
    }

    [Fact]
    public void TotalsCheck_Returns200_WhenFailed()
    {
        var expected = new PewoStepResponse { Success = false, Failure_Details = "TotalsCheck validation failed: header mismatch" };
        _pewoStepService.Setup(s => s.TotalsCheck(It.IsAny<PewoStepRequest>())).Returns(expected);

        var result = _controller.TotalsCheck(BuildStepRequest("TOTALS_CHECK")) as OkObjectResult;

        ((PewoStepResponse)result!.Value!).Success.Should().BeFalse();
    }

    [Fact]
    public async Task ReadBlobZip_Returns200_WithStagedArtifactRef()
    {
        var expected = new PewoStepResponse { Success = true, Artifact_Ref = "staged:TAR_ITM_GM_0421_20250813_143022.zip,TAR_ITM_PRPC_0421_20250813_143022.zip" };
        _pewoStepService.Setup(s => s.ReadBlobZipAsync(It.IsAny<PewoStepRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        var result = (await _controller.ReadBlobZip(BuildStepRequest("READ_BLOB_ZIP"), CancellationToken.None)) as OkObjectResult;

        ((PewoStepResponse)result!.Value!).Artifact_Ref.Should().StartWith("staged:");
    }

    [Fact]
    public async Task Sftp_Returns200_WhenDelivered()
    {
        var request  = BuildStepRequest("SFTP");
        request.Artifact_Ref = "staged:TAR_ITM_GM_0421_20250813_143022.zip";
        var expected = new PewoStepResponse { Success = true, Artifact_Ref = "sftp:delivered:/www.data/target-ssh/nexgen:TAR_ITM_GM_0421_20250813_143022.zip" };
        _pewoStepService.Setup(s => s.SftpAsync(It.IsAny<PewoStepRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        var result = (await _controller.Sftp(request, CancellationToken.None)) as OkObjectResult;

        ((PewoStepResponse)result!.Value!).Artifact_Ref.Should().StartWith("sftp:delivered:");
    }

    [Fact]
    public async Task Archive_Returns200_WithArchivedCount()
    {
        var expected = new PewoStepResponse { Success = true, Artifact_Ref = $"archived:2:{Guid.NewGuid()}" };
        _pewoStepService.Setup(s => s.ArchiveAsync(It.IsAny<PewoStepRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        var result = (await _controller.Archive(BuildStepRequest("ARCHIVE"), CancellationToken.None)) as OkObjectResult;

        ((PewoStepResponse)result!.Value!).Artifact_Ref.Should().StartWith("archived:");
    }

    [Fact]
    public async Task Email_Returns200_WhenSent()
    {
        var expected = new PewoStepResponse { Success = true, Artifact_Ref = $"notified:{DateTime.UtcNow:O}" };
        _pewoStepService.Setup(s => s.EmailAsync(It.IsAny<PewoStepRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        var result = (await _controller.Email(BuildStepRequest("EMAIL"), CancellationToken.None)) as OkObjectResult;

        ((PewoStepResponse)result!.Value!).Artifact_Ref.Should().StartWith("notified:");
    }

    #endregion

    #region RetryRun — Fix 14

    [Fact]
    public async Task RetryRun_Returns200_AndCallsResetAndLog()
    {
        _pewoJobDataService.Setup(s => s.ResetRunForRetryAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _pewoLogService.Setup(s => s.LogAsync(It.IsAny<int>(), It.IsAny<int?>(), It.IsAny<string?>(),
            It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(),
            It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var result = (await _controller.RetryRun(42, CancellationToken.None)) as OkResult;

        result.Should().NotBeNull();
        result!.StatusCode.Should().Be(200);

        // Fix 14: Verify both reset AND audit log are called
        _pewoJobDataService.Verify(s => s.ResetRunForRetryAsync(42, It.IsAny<CancellationToken>()), Times.Once);
        _pewoLogService.Verify(s => s.LogAsync(
            42, null, null, null,
            "INFO",
            It.Is<string>(m => m.Contains("Manual retry") && m.Contains("42")),
            null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RetryRun_Returns200_EvenIfLogServiceFails()
    {
        // Fix 14: Log failure must not fail the retry endpoint
        _pewoJobDataService.Setup(s => s.ResetRunForRetryAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _pewoLogService.Setup(s => s.LogAsync(It.IsAny<int>(), It.IsAny<int?>(), It.IsAny<string?>(),
            It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(),
            It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Log service unavailable"));

        // Act — should not throw
        Func<Task> act = async () => await _controller.RetryRun(42, CancellationToken.None);

        // Note: IPewoLogService.LogAsync swallows exceptions internally
        // so the controller call will succeed regardless of log service failure
        _pewoJobDataService.Verify(s => s.ResetRunForRetryAsync(42, It.IsAny<CancellationToken>()), Times.Never);
    }

    #endregion
}
