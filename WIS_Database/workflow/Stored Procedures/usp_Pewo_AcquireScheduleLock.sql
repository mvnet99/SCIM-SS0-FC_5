-- =============================================================================
-- usp_Pewo_AcquireScheduleLock
-- Atomic CAS: ACTIVE → RUNNING.  Returns 1 if lock acquired, 0 if missed.
-- Called by POST /api/pewo/runs/{id}/lock
-- =============================================================================
CREATE PROCEDURE [workflow].[usp_Pewo_AcquireScheduleLock]
    @scheduleId INT,
    @workerId   VARCHAR(100)
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE workflow.Schedule
    SET    status    = 'RUNNING',
           locked_by = @workerId,
           locked_at = SYSUTCDATETIME(),
           updated_at = SYSUTCDATETIME()
    WHERE  schedule_id  = @scheduleId
    AND    status       = 'ACTIVE'
    AND    next_run_at <= SYSUTCDATETIME()
    AND    is_enabled   = 1;

    SELECT @@ROWCOUNT AS rows_affected;
END;
GO
