CREATE OR ALTER PROCEDURE dbo.usp_Pewo_GetBatchRunStatus
    @WorkflowTypeCode NVARCHAR(100),
    @BatchKey         NVARCHAR(50)
AS
BEGIN
    SET NOCOUNT ON;
    SELECT
        wr.id_WorkflowRun,
        wr.Batch_Key,
        wre.Store_No,
        wre.Store_Name,
        wre.Event_Date,
        wr.Status,
        wr.Reason
    FROM      dbo.Pewo_WorkflowRun wr
    LEFT JOIN dbo.Pewo_WorkflowRunEvent wre ON wre.id_WorkflowRun = wr.id_WorkflowRun
    JOIN      dbo.Pewo_CustomerWorkflowType cwt
              ON cwt.id_CustomerWorkflowType = wr.id_CustomerWorkflowType
    WHERE  cwt.WorkflowType_Code = @WorkflowTypeCode
    AND    wr.Batch_Key          = @BatchKey;
END
