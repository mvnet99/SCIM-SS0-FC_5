using WS.FC.Dapper.Shared.DTOs.Pewo;
using WS.FC.Dapper.Shared.Queries.Core;

namespace WS.FC.Dapper.Shared.Queries.Pewo;

public class GetDueJobsQuery : BaseQuery<List<DueJobDto>>
{
}

public class GetRunResumeQuery : BaseQuery<List<RunResumeStepDto>>
{
    public int Id_WorkflowRun { get; set; }
    public int Id_CustomerWorkflowType { get; set; }
}

public class GetWorkflowRunEventsQuery : BaseQuery<List<WorkflowRunEventDto>>
{
    public int Id_WorkflowRun { get; set; }
}

public class GetBatchRunStatusQuery : BaseQuery<List<BatchRunStatusDto>>
{
    public string WorkflowTypeCode { get; set; } = string.Empty;
    public string BatchKey { get; set; } = string.Empty;
}
