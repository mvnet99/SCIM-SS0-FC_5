-- =============================================================================
-- usp_Pewo_GetDueJobs
-- Returns Schedule rows due to fire plus failed WorkflowRuns due for auto-retry.
-- Called by GET /api/pewo/jobs/due
-- =============================================================================
CREATE PROCEDURE [workflow].[usp_Pewo_GetDueJobs]
AS
BEGIN
    SET NOCOUNT ON;

    -- Due schedules (not currently locked by a worker)
    SELECT
        s.schedule_id,
        s.workflow_type_id,
        s.schedule_name,
        s.cron_expression,
        s.timezone,
        s.next_run_at,
        s.status,
        s.last_run_id,
        wt.workflow_code,
        wt.workflow_name,
        wt.max_retries,
        NULL AS run_id,
        'SCHEDULE' AS job_source
    FROM   workflow.Schedule s
    JOIN   workflow.WorkflowType wt ON wt.workflow_type_id = s.workflow_type_id
    WHERE  s.status       = 'ACTIVE'
    AND    s.next_run_at <= SYSUTCDATETIME()
    AND    s.is_enabled   = 1

    UNION ALL

    -- Failed runs due for auto-retry
    SELECT
        s.schedule_id,
        wr.workflow_type_id,
        s.schedule_name,
        s.cron_expression,
        s.timezone,
        s.next_run_at,
        s.status,
        s.last_run_id,
        wt.workflow_code,
        wt.workflow_name,
        wr.max_retries,
        wr.run_id,
        'RETRY' AS job_source
    FROM   workflow.WorkflowRun wr
    JOIN   workflow.Schedule     s  ON s.schedule_id      = wr.schedule_id
    JOIN   workflow.WorkflowType wt ON wt.workflow_type_id = wr.workflow_type_id
    WHERE  wr.status      = 'FAILED'
    AND    wr.retry_at   <= SYSUTCDATETIME()
    AND    wr.retry_count < wr.max_retries;
END;
GO
