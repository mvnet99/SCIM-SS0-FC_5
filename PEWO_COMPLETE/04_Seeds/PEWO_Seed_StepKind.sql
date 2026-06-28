-- =============================================
-- DACPAC Post-Deployment Script
-- Table:   dbo.Pewo_StepKind
-- Purpose: Seed lookup values for all valid PEWO step kinds.
--          These values must match exactly the Step_Kind string
--          constants in PewoWorkerService.cs switch statement.
--          Rerunnable — safe to execute multiple times.
-- =============================================

MERGE dbo.Pewo_StepKind AS target
USING (
    VALUES
        ('TOTALS_CHECK'),
        ('GET_EVENTS'),
        ('TRANSFORM'),
        ('READ_BLOB_ZIP'),
        ('SFTP'),
        ('ARCHIVE'),
        ('EMAIL'),
        ('EMAIL_SUMMARY')
) AS source (Step_Kind)
ON target.Step_Kind = source.Step_Kind
WHEN NOT MATCHED BY TARGET THEN
    INSERT (Step_Kind)
    VALUES (source.Step_Kind);
