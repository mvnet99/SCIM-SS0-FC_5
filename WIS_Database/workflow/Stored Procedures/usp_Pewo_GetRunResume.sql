-- =============================================================================
-- usp_Pewo_GetRunResume
-- Resume query: LEFT JOIN StepRun to WorkflowStepDef for a given run.
-- status=SUCCESS → skip, status=FAILED or NULL → execute.
-- Called by GET /api/pewo/runs/{runId}
-- =============================================================================
CREATE PROCEDURE [workflow].[usp_Pewo_GetRunResume]
    @runId            UNIQUEIDENTIFIER,
    @workflowTypeId   INT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        sd.step_def_id,
        sd.step_order,
        sd.step_kind,
        sd.step_name,
        sd.config,
        sd.max_attempts,
        sd.backoff_seconds,
        sr.step_run_id,
        sr.status,
        sr.attempts,
        sr.reason,
        sr.artifact_ref
    FROM      workflow.WorkflowStepDef sd
    LEFT JOIN workflow.StepRun          sr
        ON  sr.step_def_id = sd.step_def_id
        AND sr.run_id      = @runId
    WHERE sd.workflow_type_id = @workflowTypeId
    AND   sd.is_active        = 1
    ORDER BY sd.step_order ASC;
END;
GO
