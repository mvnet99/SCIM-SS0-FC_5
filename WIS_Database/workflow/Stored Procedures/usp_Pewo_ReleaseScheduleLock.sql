-- =============================================================================
-- usp_Pewo_ReleaseScheduleLock
-- Release the schedule lock after a run completes (success or failure).
-- Advances next_run_at, records last_run_id and last_status.
-- Called by POST /api/pewo/runs/{id}/lock with release=true
-- =============================================================================
CREATE PROCEDURE [workflow].[usp_Pewo_ReleaseScheduleLock]
    @scheduleId   INT,
    @workerId     VARCHAR(100),
    @nextRunAt    DATETIME2,
    @lastRunId    UNIQUEIDENTIFIER,
    @lastStatus   VARCHAR(20)
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE workflow.Schedule
    SET    status      = 'ACTIVE',
           locked_by   = NULL,
           locked_at   = NULL,
           last_run_at = SYSUTCDATETIME(),
           next_run_at = @nextRunAt,
           last_run_id = @lastRunId,
           last_status = @lastStatus,
           updated_at  = SYSUTCDATETIME()
    WHERE  schedule_id = @scheduleId
    AND    locked_by   = @workerId;

    SELECT @@ROWCOUNT AS rows_affected;
END;
GO
