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

    public readonly Mock<IPewoJobDataService>        _jobDataService = new();
    public readonly Mock<IPewoLogService>            _logService     = new();
    public readonly Mock<IPewoStepService>           _stepService    = new();
    public readonly Mock<ILogger<PewoWorkerService>> _logger         = new();

    public PewoWorkerServiceTest()
    {
        _service = new PewoWorkerService(
            _jobDataService.Object, _logService.Object,
            _stepService.Object, _logger.Object);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static DueJobDto BuildJob(
        string code = "GM_PRC_DELIVERY",
        string source = "SCHEDULE",
        int? runId = null,
        string cron = "0 8 * * *") =>
        new()
        {
            Id_Schedule = 1, Id_CustomerWorkflowType = 5,
            Schedule_Name = "Test", Cron_Expression = cron, Timezone = "UTC",
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

        if (job.Job_Source == "SCHEDULE" || job.Job_Source == "SAFETY_NET")
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

    // ── Existing tests ────────────────────────────────────────────────────────

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
    public async Task RunAsync_SkipsCompletedSteps_ResumeNotRestart()
    {
        var job           = BuildJob(source: "SCHEDULE");
        var completedStep = BuildStep("READ_BLOB_ZIP", status: "COMPLETED", order: 1);
        var pendingStep   = BuildStep("SFTP", order: 2);
        pendingStep.Id_WorkflowStepDef = 2;

        SetupCommonMocks(42, job, new List<RunResumeStepDto> { completedStep, pendingStep });

        _stepService.Setup(s => s.SftpAsync(It.IsAny<PewoStepRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PewoStepResponse { Success = true, Artifact_Ref = "sftp:delivered:file.zip" });

        await _service.RunAsync(CancellationToken.None);

        _stepService.Verify(s => s.ReadBlobZipAsync(It.IsAny<PewoStepRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        _stepService.Verify(s => s.SftpAsync(It.IsAny<PewoStepRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── Fix 9: AdvanceSchedule skipped for failed ON_EVENT_CLOSE runs ─────────

    [Fact]
    public async Task RunAsync_Fix9_SkipsAdvanceSchedule_WhenOnEventCloseRunFails()
    {
        // ON_EVENT_CLOSE cron — schedule is shared, must not be advanced on failure
        var job  = BuildJob(source: "SCHEDULE", cron: "ON_EVENT_CLOSE");
        var step = BuildStep("TOTALS_CHECK");
        SetupCommonMocks(42, job, new List<RunResumeStepDto> { step });

        _stepService.Setup(s => s.TotalsCheck(It.IsAny<PewoStepRequest>()))
            .Returns(new PewoStepResponse { Success = false, Failure_Details = "Validation failed" });

        await _service.RunAsync(CancellationToken.None);

        // AdvanceSchedule must NOT be called when ON_EVENT_CLOSE run fails
        _jobDataService.Verify(s => s.AdvanceScheduleAsync(
            It.IsAny<int>(), It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RunAsync_Fix9_CallsAdvanceSchedule_WhenOnEventCloseRunSucceeds()
    {
        // ON_EVENT_CLOSE run that succeeds — advance to 50 years
        var job  = BuildJob(source: "SCHEDULE", cron: "ON_EVENT_CLOSE");
        var step = BuildStep("TOTALS_CHECK");
        SetupCommonMocks(42, job, new List<RunResumeStepDto> { step });

        _stepService.Setup(s => s.TotalsCheck(It.IsAny<PewoStepRequest>()))
            .Returns(new PewoStepResponse { Success = true, Artifact_Ref = "{\"totalQty\":1000}" });

        await _service.RunAsync(CancellationToken.None);

        _jobDataService.Verify(s => s.AdvanceScheduleAsync(
            It.IsAny<int>(), It.IsAny<DateTime>(), It.IsAny<int>(), "COMPLETED",
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RunAsync_Fix9_AlwaysCallsAdvanceSchedule_ForCronRuns()
    {
        // Cron schedule (GM_PRC_DELIVERY) — always advances regardless of result
        var job  = BuildJob(source: "SCHEDULE", cron: "0 8 * * *");
        var step = BuildStep("SFTP");
        SetupCommonMocks(42, job, new List<RunResumeStepDto> { step });

        _stepService.Setup(s => s.SftpAsync(It.IsAny<PewoStepRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PewoStepResponse { Success = false, Failure_Details = "SFTP failed" });

        await _service.RunAsync(CancellationToken.None);

        // Cron schedule MUST advance even on failure
        _jobDataService.Verify(s => s.AdvanceScheduleAsync(
            It.IsAny<int>(), It.IsAny<DateTime>(), It.IsAny<int>(), "FAILED",
            It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── Fix 15: GetDueJobsAsync failure handled gracefully ───────────────────

    [Fact]
    public async Task RunAsync_Fix15_ReturnsEmptyResponse_WhenGetDueJobsFails()
    {
        _jobDataService.Setup(s => s.GetDueJobsAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("SQL Server connection timeout"));

        var result = await _service.RunAsync(CancellationToken.None);

        // Should not throw — returns empty response and logs error
        result.JobsProcessed.Should().Be(0);
        result.JobsSucceeded.Should().Be(0);
        result.JobsFailed.Should().Be(0);

        // No runs should be created or processed
        _jobDataService.Verify(s => s.CreateWorkflowRunAsync(
            It.IsAny<int>(), It.IsAny<int>(), It.IsAny<short>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RunAsync_Fix15_ContinuesNextJobsAfterSingleJobException()
    {
        var job1 = BuildJob(code: "GM_TOTALS_CHECK", source: "SCHEDULE", cron: "ON_EVENT_CLOSE");
        var job2 = BuildJob(code: "GM_PRC_DELIVERY", source: "SCHEDULE");

        _jobDataService.Setup(s => s.GetDueJobsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<DueJobDto> { job1, job2 });

        // job1 causes unhandled exception
        _jobDataService.SetupSequence(s => s.CreateWorkflowRunAsync(
            It.IsAny<int>(), It.IsAny<int>(), It.IsAny<short>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Database error"))
            .ReturnsAsync(43);

        _jobDataService.Setup(s => s.GetRunResumeAsync(43, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RunResumeStepDto> { BuildStep("EMAIL") });
        _jobDataService.Setup(s => s.GetWorkflowRunEventsAsync(43, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<WorkflowRunEventDto> { BuildEvent() });
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

        _stepService.Setup(s => s.EmailAsync(It.IsAny<PewoStepRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PewoStepResponse { Success = true, Artifact_Ref = $"notified:{DateTime.UtcNow:O}" });

        var result = await _service.RunAsync(CancellationToken.None);

        // job1 failed, job2 succeeded — total 2 processed
        result.JobsProcessed.Should().Be(2);
        result.JobsFailed.Should().Be(1);
    }

    // ── Fix 16: Artifact_Ref length guard ─────────────────────────────────────

    [Fact]
    public async Task RunAsync_Fix16_TruncatesLongArtifactRef_WithWarningLog()
    {
        var job  = BuildJob(source: "SCHEDULE");
        var step = BuildStep("READ_BLOB_ZIP");
        SetupCommonMocks(42, job, new List<RunResumeStepDto> { step });

        // Generate artifact_ref longer than 490 chars
        var longArtifactRef = "staged:" + new string('A', 500);
        longArtifactRef.Length.Should().BeGreaterThan(490);

        _stepService.Setup(s => s.ReadBlobZipAsync(It.IsAny<PewoStepRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PewoStepResponse { Success = true, Artifact_Ref = longArtifactRef });

        string? capturedArtifactRef = null;
        _jobDataService.Setup(s => s.UpsertStepRunAsync(
            It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<short>(), It.IsAny<string?>(), It.IsAny<string?>(),
            It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
            .Callback<int, int, string, string, short, string?, string?, DateTime?, DateTime?, CancellationToken>(
                (_, _, _, _, _, ar, _, _, _, _) => capturedArtifactRef = ar)
            .Returns(Task.CompletedTask);

        await _service.RunAsync(CancellationToken.None);

        capturedArtifactRef.Should().NotBeNull();
        capturedArtifactRef!.Length.Should().BeLessThanOrEqualTo(490);
    }

    [Fact]
    public async Task RunAsync_Fix16_DoesNotTruncate_WhenArtifactRefWithinLimit()
    {
        var job  = BuildJob(source: "SCHEDULE");
        var step = BuildStep("EMAIL");
        SetupCommonMocks(42, job, new List<RunResumeStepDto> { step });

        var shortArtifactRef = $"notified:{DateTime.UtcNow:O}";

        _stepService.Setup(s => s.EmailAsync(It.IsAny<PewoStepRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PewoStepResponse { Success = true, Artifact_Ref = shortArtifactRef });

        string? capturedArtifactRef = null;
        _jobDataService.Setup(s => s.UpsertStepRunAsync(
            It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<short>(), It.IsAny<string?>(), It.IsAny<string?>(),
            It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
            .Callback<int, int, string, string, short, string?, string?, DateTime?, DateTime?, CancellationToken>(
                (_, _, _, _, _, ar, _, _, _, _) => capturedArtifactRef = ar)
            .Returns(Task.CompletedTask);

        await _service.RunAsync(CancellationToken.None);

        capturedArtifactRef.Should().Be(shortArtifactRef);
    }

    // ── Fix 18: Dead letter email on max retries ──────────────────────────────

    [Fact]
    public async Task RunAsync_Fix18_SendsDeadLetterEmail_WhenMaxRetriesExhausted()
    {
        var job  = BuildJob(source: "RETRY", runId: 42, cron: "0 8 * * *");
        job.Max_Retries = 2;
        var emailStep   = BuildStep("EMAIL", order: 2);
        emailStep.Config = "{\"recipients\":\"ops@company.com\",\"subject\":\"Test\"}";
        var failStep    = BuildStep("SFTP", order: 1);
        failStep.Attempts = 2; // Already at max

        _jobDataService.Setup(s => s.GetDueJobsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<DueJobDto> { job });
        _jobDataService.Setup(s => s.GetRunResumeAsync(42, job.Id_CustomerWorkflowType, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RunResumeStepDto> { failStep, emailStep });
        _jobDataService.Setup(s => s.GetWorkflowRunEventsAsync(42, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<WorkflowRunEventDto> { BuildEvent() });
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

        _stepService.Setup(s => s.SftpAsync(It.IsAny<PewoStepRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PewoStepResponse { Success = false, Failure_Details = "Connection refused" });

        PewoStepRequest? deadLetterRequest = null;
        _stepService.Setup(s => s.EmailAsync(It.IsAny<PewoStepRequest>(), It.IsAny<CancellationToken>()))
            .Callback<PewoStepRequest, CancellationToken>((r, _) => deadLetterRequest = r)
            .ReturnsAsync(new PewoStepResponse { Success = true });

        await _service.RunAsync(CancellationToken.None);

        // Dead letter email must be sent when max retries exceeded
        _stepService.Verify(s => s.EmailAsync(It.IsAny<PewoStepRequest>(), It.IsAny<CancellationToken>()), Times.Once);
        deadLetterRequest.Should().NotBeNull();
        deadLetterRequest!.Config.Should().Contain("FAILED");
        deadLetterRequest.Config.Should().Contain("Max retries exceeded");
    }

    [Fact]
    public async Task RunAsync_Fix18_DoesNotSendDeadLetter_WhenRetriesRemaining()
    {
        var job  = BuildJob(source: "RETRY", runId: 42);
        job.Max_Retries = 3;
        var step = BuildStep("SFTP");
        step.Attempts = 1; // Only 1 attempt used, 2 remaining

        _jobDataService.Setup(s => s.GetDueJobsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<DueJobDto> { job });
        _jobDataService.Setup(s => s.GetRunResumeAsync(42, job.Id_CustomerWorkflowType, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RunResumeStepDto> { step });
        _jobDataService.Setup(s => s.GetWorkflowRunEventsAsync(42, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<WorkflowRunEventDto> { BuildEvent() });
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

        _stepService.Setup(s => s.SftpAsync(It.IsAny<PewoStepRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PewoStepResponse { Success = false, Failure_Details = "Timeout" });

        await _service.RunAsync(CancellationToken.None);

        // No dead letter — retries still remaining
        _stepService.Verify(s => s.EmailAsync(It.IsAny<PewoStepRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
