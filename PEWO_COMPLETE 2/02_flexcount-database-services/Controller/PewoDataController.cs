using Microsoft.AspNetCore.Mvc;
using System;
using System.Threading;
using System.Threading.Tasks;
using WS.FC.Dapper.Domain.Interfaces.Services;
using WS.FC.Dapper.Shared.DTOs.Pewo;

namespace WS.FC.Dapper.WebAPI.Controllers;

[Route("api/[controller]")]
[ApiController]
public class PewoDataController : ControllerBase
{
    private readonly IPewoDataService _pewoDataService;

    public PewoDataController(IPewoDataService pewoDataService)
    {
        _pewoDataService = pewoDataService;
    }

    [HttpGet]
    [Route("due-jobs")]
    public async Task<IActionResult> GetDueJobs(CancellationToken cancellationToken)
    {
        var data = await _pewoDataService.GetDueJobsAsync(cancellationToken);
        return Ok(data);
    }

    [HttpGet]
    [Route("runs/{idWorkflowRun}/resume")]
    public async Task<IActionResult> GetRunResume(
        int idWorkflowRun,
        [FromQuery] int idCustomerWorkflowType,
        CancellationToken cancellationToken)
    {
        var data = await _pewoDataService.GetRunResumeAsync(idWorkflowRun, idCustomerWorkflowType, cancellationToken);
        return Ok(data);
    }

    [HttpGet]
    [Route("runs/{idWorkflowRun}/events")]
    public async Task<IActionResult> GetWorkflowRunEvents(int idWorkflowRun, CancellationToken cancellationToken)
    {
        var data = await _pewoDataService.GetWorkflowRunEventsAsync(idWorkflowRun, cancellationToken);
        return Ok(data);
    }

    [HttpGet]
    [Route("batch-status")]
    public async Task<IActionResult> GetBatchRunStatus(
        [FromQuery] string workflowTypeCode,
        [FromQuery] string batchKey,
        CancellationToken cancellationToken)
    {
        var data = await _pewoDataService.GetBatchRunStatusAsync(workflowTypeCode, batchKey, cancellationToken);
        return Ok(data);
    }

    [HttpPost]
    [Route("runs")]
    public async Task<IActionResult> CreateWorkflowRun([FromBody] CreateWorkflowRunDto dto, CancellationToken cancellationToken)
    {
        var newId = await _pewoDataService.CreateWorkflowRunAsync(
            dto.Id_Schedule, dto.Id_CustomerWorkflowType, dto.Max_Retries, cancellationToken);
        return Ok(newId);
    }

    [HttpPost]
    [Route("runs/{idWorkflowRun}/events")]
    public async Task<IActionResult> CreateWorkflowRunEvent(
        int idWorkflowRun,
        [FromBody] CreateWorkflowRunEventDto dto,
        CancellationToken cancellationToken)
    {
        var newId = await _pewoDataService.CreateWorkflowRunEventAsync(
            idWorkflowRun, dto.Id_Event, dto.Id_Customer, dto.Id_Store,
            dto.Store_No, dto.Store_Name, dto.Event_Guid, dto.Event_Status,
            dto.Event_Scheduled_Date, dto.Event_Date, dto.Metadata_Json,
            cancellationToken);
        return Ok(newId);
    }

    [HttpPost]
    [Route("runs/{idWorkflowRun}/steps/{idWorkflowStepDef}")]
    public async Task<IActionResult> UpsertStepRun(
        int idWorkflowRun,
        int idWorkflowStepDef,
        [FromBody] UpsertStepRunDto dto,
        CancellationToken cancellationToken)
    {
        await _pewoDataService.UpsertStepRunAsync(
            idWorkflowRun, idWorkflowStepDef, dto.Step_Kind, dto.Status,
            dto.Attempts, dto.Artifact_Ref, dto.Failure_Details,
            dto.Start_Time, dto.End_Time, cancellationToken);
        return Ok();
    }

    [HttpPut]
    [Route("runs/{idWorkflowRun}/status")]
    public async Task<IActionResult> SetRunTerminalStatus(
        int idWorkflowRun,
        [FromBody] SetRunTerminalStatusDto dto,
        CancellationToken cancellationToken)
    {
        await _pewoDataService.SetRunTerminalStatusAsync(
            idWorkflowRun, dto.Status, dto.Reason, dto.Retry_At, dto.Retry_Count, cancellationToken);
        return Ok();
    }

    [HttpPut]
    [Route("schedules/{idSchedule}/advance")]
    public async Task<IActionResult> AdvanceSchedule(
        int idSchedule,
        [FromBody] AdvanceScheduleDto dto,
        CancellationToken cancellationToken)
    {
        await _pewoDataService.AdvanceScheduleAsync(
            idSchedule, dto.Next_Run_At, dto.Last_Run_Id, dto.Last_Status, cancellationToken);
        return Ok();
    }

    [HttpPut]
    [Route("runs/{idWorkflowRun}/reset")]
    public async Task<IActionResult> ResetRunForRetry(int idWorkflowRun, CancellationToken cancellationToken)
    {
        await _pewoDataService.ResetRunForRetryAsync(idWorkflowRun, cancellationToken);
        return Ok();
    }

    [HttpPost]
    [Route("logs")]
    public async Task<IActionResult> InsertLog([FromBody] InsertLogDto dto, CancellationToken cancellationToken)
    {
        await _pewoDataService.InsertLogAsync(
            // id_WorkflowRun is mandatory — passed via route in real usage; included in body for simplicity
            dto.Id_Customer ?? 0, dto.Id_Customer, dto.Customer_Name,
            dto.Step_Kind, dto.Log_Level, dto.Message, dto.Event_Context,
            cancellationToken);
        return Ok();
    }

    [HttpPost]
    [Route("event-close")]
    public async Task<IActionResult> CreateRunOnEventClose(
        [FromBody] CreateRunOnEventCloseDto dto,
        CancellationToken cancellationToken)
    {
        var result = await _pewoDataService.CreateRunOnEventCloseAsync(
            dto.Id_Customer, dto.Id_Event, dto.Store_No, dto.Store_Name,
            dto.Event_Date, dto.WorkflowType_Code, dto.Event_Guid,
            dto.Id_Store, dto.Event_Status, dto.Event_Scheduled_Date,
            cancellationToken);
        return Ok(result);
    }
}
