CREATE TABLE [workflow].[JobEvent] (
    [job_event_id]      INT              NOT NULL IDENTITY(1,1),
    [run_id]            UNIQUEIDENTIFIER NOT NULL,
    [event_id]          INT              NOT NULL,
    [store_no]          VARCHAR(10)      NOT NULL,
    [store_name]        NVARCHAR(200)    NULL,
    [event_date]        DATE             NULL,
    [file_pattern_gm]   VARCHAR(200)     NULL,
    [file_pattern_prpc] VARCHAR(200)     NULL,
    [metadata_json]     NVARCHAR(MAX)    NULL,
    [created_at]        DATETIME2        NOT NULL DEFAULT SYSUTCDATETIME(),

    CONSTRAINT [PK_JobEvent]             PRIMARY KEY ([job_event_id]),
    CONSTRAINT [FK_JobEvent_WorkflowRun] FOREIGN KEY ([run_id])
                                         REFERENCES [workflow].[WorkflowRun]([run_id])
);
GO

CREATE INDEX [IX_JobEvent_RunId]
    ON [workflow].[JobEvent] ([run_id])
    INCLUDE ([event_id], [store_no], [file_pattern_gm], [file_pattern_prpc]);
GO
