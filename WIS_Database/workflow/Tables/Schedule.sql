CREATE TABLE [workflow].[Schedule] (
    [schedule_id]      INT              NOT NULL IDENTITY(1,1),
    [workflow_type_id] INT              NOT NULL,
    [schedule_name]    NVARCHAR(200)    NOT NULL,
    [cron_expression]  VARCHAR(100)     NOT NULL,
    [timezone]         VARCHAR(100)     NOT NULL DEFAULT 'UTC',
    [next_run_at]      DATETIME2        NULL,
    [last_run_at]      DATETIME2        NULL,
    [status]           VARCHAR(20)      NOT NULL DEFAULT 'ACTIVE',
    [locked_by]        VARCHAR(100)     NULL,
    [locked_at]        DATETIME2        NULL,
    [last_status]      VARCHAR(20)      NULL,
    [last_run_id]      UNIQUEIDENTIFIER NULL,
    [last_error]       NVARCHAR(MAX)    NULL,
    [is_enabled]       BIT              NOT NULL DEFAULT 1,
    [created_at]       DATETIME2        NOT NULL DEFAULT SYSUTCDATETIME(),
    [updated_at]       DATETIME2        NOT NULL DEFAULT SYSUTCDATETIME(),

    CONSTRAINT [PK_Schedule]              PRIMARY KEY ([schedule_id]),
    CONSTRAINT [FK_Schedule_WorkflowType] FOREIGN KEY ([workflow_type_id])
                                          REFERENCES [workflow].[WorkflowType]([workflow_type_id]),
    CONSTRAINT [CK_Schedule_Status]       CHECK ([status] IN (
                                              'ACTIVE',
                                              'RUNNING',
                                              'SUSPENDED',
                                              'DISABLED'
                                          ))
);
GO

CREATE INDEX [IX_Schedule_Due]
    ON [workflow].[Schedule] ([status], [next_run_at])
    WHERE [is_enabled] = 1;
GO
