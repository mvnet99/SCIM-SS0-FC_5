CREATE TABLE [workflow].[WorkflowRun] (
    [run_id]           UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID(),
    [workflow_type_id] INT              NOT NULL,
    [schedule_id]      INT              NOT NULL,
    [status]           VARCHAR(20)      NOT NULL DEFAULT 'PENDING',
    [reason]           NVARCHAR(MAX)    NULL,
    [locked_by]        VARCHAR(100)     NULL,
    [locked_at]        DATETIME2        NULL,
    [retry_at]         DATETIME2        NULL,
    [retry_count]      INT              NOT NULL DEFAULT 0,
    [max_retries]      INT              NOT NULL DEFAULT 3,
    [created_at]       DATETIME2        NOT NULL DEFAULT SYSUTCDATETIME(),
    [started_at]       DATETIME2        NULL,
    [finished_at]      DATETIME2        NULL,

    CONSTRAINT [PK_WorkflowRun]              PRIMARY KEY ([run_id]),
    CONSTRAINT [FK_WorkflowRun_WorkflowType] FOREIGN KEY ([workflow_type_id])
                                             REFERENCES [workflow].[WorkflowType]([workflow_type_id]),
    CONSTRAINT [FK_WorkflowRun_Schedule]     FOREIGN KEY ([schedule_id])
                                             REFERENCES [workflow].[Schedule]([schedule_id]),
    CONSTRAINT [CK_WorkflowRun_Status]       CHECK ([status] IN (
                                                 'PENDING',
                                                 'RUNNING',
                                                 'COMPLETED',
                                                 'FAILED'
                                             ))
);
GO

CREATE INDEX [IX_WorkflowRun_Retry]
    ON [workflow].[WorkflowRun] ([status], [retry_at])
    WHERE [status] = 'FAILED';
GO

CREATE INDEX [IX_WorkflowRun_Lock]
    ON [workflow].[WorkflowRun] ([locked_by], [locked_at])
    WHERE [locked_by] IS NOT NULL;
GO
