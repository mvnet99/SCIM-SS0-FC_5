CREATE OR ALTER PROCEDURE dbo.usp_Pewo_GetRunResume
    @id_WorkflowRun         INT,
    @id_CustomerWorkflowType INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT
        wsd.id_WorkflowStepDef,
        wsd.id_CustomerWorkflowType,
        wsd.Step_Order,
        wsd.Step_Kind,
        wsd.Step_Name,
        wsd.Config,
        wsd.Max_Attempts,
        wsd.Backoff_Seconds,
        wsr.id_WorkflowStepRun,
        wsr.Status,
        ISNULL(wsr.Attempts, 0) AS Attempts,
        wsr.Artifact_Ref,
        wsr.Failure_Details
    FROM      dbo.Pewo_WorkflowStepDef wsd
    LEFT JOIN dbo.Pewo_WorkflowStepRun wsr
              ON wsr.id_WorkflowStepDef = wsd.id_WorkflowStepDef
              AND wsr.id_WorkflowRun    = @id_WorkflowRun
    WHERE wsd.id_CustomerWorkflowType = @id_CustomerWorkflowType
    AND   wsd.is_Active = 1
    ORDER BY wsd.Step_Order;
END
