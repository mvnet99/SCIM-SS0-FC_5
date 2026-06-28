using Domain.ApiModels.Pewo;
using Domain.Services.Interfaces.Pewo;
using Domain.Services.Pewo;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using WS.FC.Dapper.Shared.DTOs.Pewo;
using Xunit;

namespace Tests.System.Services;

public class PewoWorkerServiceTest
{
    private readonly PewoWorkerService _service;

    public readonly Mock<IPewoJobDataService>       _jobDataService = new();
    public readonly Mock<IPewoLogService>           _logService     = new();
    public readonly Mock<IPewoStepService>          _stepService    = new();
    public readonly Mock<ILogger<PewoWorkerService>> _logger        = new();

    public PewoWorkerServiceTest()
    {
        _service = new PewoWorkerService(
            _jobDataService.Object, _logService.Object,
            _stepService.Object, _logger.Object);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static DueJobDto BuildJob(string code = "GM_PRC_DELIVERY", string source = "SCHEDULE", int? runId = null) =>
        new()
        {
            Id_Schedule = 1, Id_CustomerWorkflowType = 5,
            Schedule_Name = "Test", Cron_Expression = "0 8 * * *", Timezone = "UTC",
            WorkflowType_Code = code, WorkflowType_Name = code,
            Max_Retries = 2, Job_Source = source, Id_WorkflowRun = runId,
            Next_Run_At = DateTime.UtcNow.AddMinutes(-1)
        };

    private static RunResumeStepDto BuildStep(string kind, string? status = null, short order = 1) =>
        new()
        {
            Id_WorkflowStepDef = (int)order, Id_CustomerWorkflowType = 5,
            Step_Order = order, Step_Kind = kind, Step_Name = kind,
            Max_Attempts = 3, Backoff_Seconds = 60, Attempts = 0, Status = status
        };

    private static WorkflowRunEventDto BuildEvent() =>
        new()
        {
            Id_WorkflowRunEvent = 1, Id_WorkflowRun = 42,
            Store_No = "T1234", Event_Guid = Guid.NewGuid(),
            Event_Date = DateTime.UtcNow.AddDays(-1)
        };

    private void SetupCommonMocks(int runId, DueJobDto job, List<RunResumeStepDto> steps)
    {
        _jobDataService.Setup(s => s.GetDueJobsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<DueJobDto> { job });

        if (job.Job_Source == "SCHEDULE")
            _jobDataService.Setup(s => s.CreateWorkflowRunAsync(
                It.IsAny<int>(), It.IsAny<int>(), It.IsAny<short>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(runId);

        _jobDataService.Setup(s => s.GetRunResumeAsync(runId, job.Id_CustomerWorkflowType, It.IsAny<CancellationToken>()))
            .ReturnsAsync(steps);

        _jobDataService.Setup(s => s.GetWorkflowRunEventsAsync(runId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<WorkflowRunEventDto> { BuildEvent() });

        _jobDataService.Setup(s => s.UpsertStepRunAsync(
            It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<short>(), It.IsAny<string?>(), It.IsAny<string?>(),
            It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _jobDataService.Setup(s => s.SetRunTerminalStatusAsync(
            It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string?>(),
            It.IsAny<DateTime?>(), It.IsAny<short>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _jobDataService.Setup(s => s.AdvanceScheduleAsync(
            It.IsAny<int>(), It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _logService.Setup(s => s.LogAsync(
            It.IsAny<int>(), It.IsAny<int?>(), It.IsAny<string?>(), It.IsAny<string?>(),
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
    }

    // ── Tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RunAsync_Returns_ZeroJobs_WhenNothingDue()
    {
        _jobDataService.Setup(s => s.GetDueJobsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<DueJobDto>());

        var result = await _service.RunAsync(CancellationToken.None);

        result.JobsProcessed.Should().Be(0);
        result.JobsSucceeded.Should().Be(0);
    }

    [Fact]
    public async Task RunAsync_CreatesNewRun_WhenSourceIsSchedule()
    {
        var job  = BuildJob(source: "SCHEDULE");
        var step = BuildStep("READ_BLOB_ZIP");
        SetupCommonMocks(42, job, new List<RunResumeStepDto> { step });

        _stepService.Setup(s => s.ReadBlobZipAsync(It.IsAny<PewoStepRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PewoStepResponse { Success = true, Artifact_Ref = "staged:file.zip" });

        var result = await _service.RunAsync(CancellationToken.None);

        result.JobsSucceeded.Should().Be(1);
        result.JobsFailed.Should().Be(0);
        _jobDataService.Verify(s => s.CreateWorkflowRunAsync(
            job.Id_Schedule, job.Id_CustomerWorkflowType, job.Max_Retries,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RunAsync_ReusesExistingRunId_WhenSourceIsRetry()
    {
        var job  = BuildJob(source: "RETRY", runId: 77);
        var step = BuildStep("SFTP");
        SetupCommonMocks(77, job, new List<RunResumeStepDto> { step });

        _stepService.Setup(s => s.SftpAsync(It.IsAny<PewoStepRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PewoStepResponse { Success = true, Artifact_Ref = "sftp:delivered:file.zip" });

        var result = await _service.RunAsync(CancellationToken.None);

        result.JobsSucceeded.Should().Be(1);
        _jobDataService.Verify(s => s.CreateWorkflowRunAsync(
            It.IsAny<int>(), It.IsAny<int>(), It.IsAny<short>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RunAsync_SetsFailedStatus_WhenStepFails()
    {
        var job  = BuildJob(source: "SCHEDULE");
        var step = BuildStep("SFTP");
        SetupCommonMocks(42, job, new List<RunResumeStepDto> { step });

        _stepService.Setup(s => s.SftpAsync(It.IsAny<PewoStepRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PewoStepResponse { Success = false, Failure_Details = "Connection refused" });

        var result = await _service.RunAsync(CancellationToken.None);

        result.JobsFailed.Should().Be(1);
        result.JobsSucceeded.Should().Be(0);
        _jobDataService.Verify(s => s.SetRunTerminalStatusAsync(
            42, "FAILED", It.IsAny<string?>(), It.IsAny<DateTime?>(), It.IsAny<short>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RunAsync_SkipsCompletedSteps_ResumeNotRestart()
    {
        var job           = BuildJob(source: "SCHEDULE");
        var completedStep = BuildStep("READ_BLOB_ZIP", status: "COMPLETED", order: 1);
        var pendingStep   = BuildStep("SFTP", order: 2);
        pendingStep.Id_WorkflowStepDef = 2;

        SetupCommonMocks(42, job, new List<RunResumeStepDto> { completedStep, pendingStep });

        _stepService.Setup(s => s.SftpAsync(It.IsAny<PewoStepRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PewoStepResponse { Success = true, Artifact_Ref = "sftp:delivered:file.zip" });

        var result = await _service.RunAsync(CancellationToken.None);

        result.JobsSucceeded.Should().Be(1);
        _stepService.Verify(s => s.ReadBlobZipAsync(It.IsAny<PewoStepRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _stepService.Verify(s => s.SftpAsync(It.IsAny<PewoStepRequest>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task RunAsync_SetsCompletedStatus_WhenAllStepsPass()
    {
        var job  = BuildJob(source: "SCHEDULE");
        var step = BuildStep("ARCHIVE");
        SetupCommonMocks(42, job, new List<RunResumeStepDto> { step });

        _stepService.Setup(s => s.ArchiveAsync(It.IsAny<PewoStepRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PewoStepResponse { Success = true, Artifact_Ref = $"archived:2:{Guid.NewGuid()}" });

        await _service.RunAsync(CancellationToken.None);

        _jobDataService.Verify(s => s.SetRunTerminalStatusAsync(
            42, "COMPLETED", null, null, 0, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RunAsync_PopulatesEventGuid_InStepRequest()
    {
        var eventGuid = Guid.NewGuid().ToString();
        var job       = BuildJob(source: "SCHEDULE");
        var step      = BuildStep("TOTALS_CHECK");

        _jobDataService.Setup(s => s.GetDueJobsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<DueJobDto> { job });
        _jobDataService.Setup(s => s.CreateWorkflowRunAsync(
            It.IsAny<int>(), It.IsAny<int>(), It.IsAny<short>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(42);
        _jobDataService.Setup(s => s.GetRunResumeAsync(42, job.Id_CustomerWorkflowType, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RunResumeStepDto> { step });
        _jobDataService.Setup(s => s.GetWorkflowRunEventsAsync(42, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<WorkflowRunEventDto>
            {
                new() { Id_WorkflowRun = 42, Store_No = "T1234",
                        Event_Guid = Guid.Parse(eventGuid), Event_Date = DateTime.UtcNow.AddDays(-1) }
            });

        PewoStepRequest? capturedRequest = null;
        _stepService.Setup(s => s.TotalsCheck(It.IsAny<PewoStepRequest>()))
            .Callback<PewoStepRequest>(r => capturedRequest = r)
            .Returns(new PewoStepResponse { Success = true });

        _jobDataService.Setup(s => s.UpsertStepRunAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<short>(), It.IsAny<string?>(), It.IsAny<string?>(),
            It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        _jobDataService.Setup(s => s.SetRunTerminalStatusAsync(It.IsAny<int>(), It.IsAny<string>(),
            It.IsAny<string?>(), It.IsAny<DateTime?>(), It.IsAny<short>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _jobDataService.Setup(s => s.AdvanceScheduleAsync(It.IsAny<int>(), It.IsAny<DateTime>(), It.IsAny<int>(),
            It.IsAny<string>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        _logService.Setup(s => s.LogAsync(It.IsAny<int>(), It.IsAny<int?>(), It.IsAny<string?>(),
            It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(),
            It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        await _service.RunAsync(CancellationToken.None);

        capturedRequest.Should().NotBeNull();
        capturedRequest!.Event_Guid.Should().Be(eventGuid);
        capturedRequest.Store_No.Should().Be("T1234");
        capturedRequest.Event_Date.Should().NotBeNull();
    }
}
