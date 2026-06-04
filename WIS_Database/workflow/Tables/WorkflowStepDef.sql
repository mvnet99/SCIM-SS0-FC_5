CREATE TABLE [workflow].[WorkflowStepDef] (
    [step_def_id]      INT             NOT NULL IDENTITY(1,1),
    [workflow_type_id] INT             NOT NULL,
    [step_order]       INT             NOT NULL,
    [step_kind]        VARCHAR(50)     NOT NULL,
    [step_name]        NVARCHAR(200)   NOT NULL,
    [config]           NVARCHAR(MAX)   NULL,
    [max_attempts]     INT             NOT NULL DEFAULT 3,
    [backoff_seconds]  INT             NOT NULL DEFAULT 30,
    [is_active]        BIT             NOT NULL DEFAULT 1,
    [created_at]       DATETIME2       NOT NULL DEFAULT SYSUTCDATETIME(),
    [updated_at]       DATETIME2       NOT NULL DEFAULT SYSUTCDATETIME(),

    CONSTRAINT [PK_WorkflowStepDef]       PRIMARY KEY ([step_def_id]),
    CONSTRAINT [FK_WorkflowStepDef_Type]  FOREIGN KEY ([workflow_type_id])
                                          REFERENCES [workflow].[WorkflowType]([workflow_type_id]),
    CONSTRAINT [UQ_WorkflowStepDef_Order] UNIQUE ([workflow_type_id], [step_order]),
    CONSTRAINT [CK_WorkflowStepDef_Kind]  CHECK ([step_kind] IN (
                                              'TOTALS_CHECK',
                                              'READ_BLOB',
                                              'ZIP',
                                              'SFTP',
                                              'ARCHIVE',
                                              'EMAIL'
                                          ))
);
GO
