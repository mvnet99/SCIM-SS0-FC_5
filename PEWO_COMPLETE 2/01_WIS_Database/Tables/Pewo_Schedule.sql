-- =============================================
-- PEWO: Schedule
-- Cron-based trigger definition per workflow type.
-- AKS CronJob / Azure Container App Job fires on schedule.
-- next_run_at drives the due-jobs query.
-- No lock columns — concurrencyPolicy: Always ensures container one pod.
--
-- Cron_Expression also accepts the value 'EVENT_PRIMED' which is used for workflows without a regular schedule
-- These only run when triggered by an external API call (e.g., GM_TOTALS_CHECK, primed by usp_Pewo_CreateRunOnEventClose when an event closes)
-- 
-- CalculateNextRunAt() recognizes this value and pushes Next_Run_At far into the future so the system never runs it automatically.
-- =============================================
CREATE TABLE [dbo].[Pewo_Schedule] (
    [id_Schedule]              INT            IDENTITY (1, 1) NOT NULL,
    [id_CustomerWorkflowType]  INT            NOT NULL,
    [Schedule_Name]            NVARCHAR (200) NOT NULL,
    [Cron_Expression]          VARCHAR (100)  NOT NULL,
    [Timezone]                 VARCHAR (100)  CONSTRAINT [df_Pewo_Schedule_Timezone] DEFAULT ('UTC') NOT NULL,
    [Next_Run_At]              DATETIME       NULL,
    [Last_Run_At]              DATETIME       NULL,
    [Status]                   VARCHAR (20)   CONSTRAINT [df_Pewo_Schedule_Status] DEFAULT ('ACTIVE') NOT NULL,
    [Last_Status]              VARCHAR (20)   NULL,
    [Last_Run_Id]              INT            NULL,
    [is_Enabled]               BIT            CONSTRAINT [df_Pewo_Schedule_is_Enabled] DEFAULT ((1)) NOT NULL,
    [created_date]             DATETIME       NULL,
    [last_updated_date]        DATETIME       NULL,
    [created_by]               INT            NULL,
    [updated_by]               INT            NULL,
    CONSTRAINT [PK_Pewo_Schedule] PRIMARY KEY CLUSTERED ([id_Schedule] ASC),
    CONSTRAINT [CK_Pewo_Schedule_Status] CHECK ([Status] IN ('ACTIVE', 'SUSPENDED', 'DISABLED')),
    CONSTRAINT [FK_Pewo_Schedule_CustomerWorkflowType] FOREIGN KEY ([id_CustomerWorkflowType])
        REFERENCES [dbo].[Pewo_CustomerWorkflowType] ([id_CustomerWorkflowType]),
    CONSTRAINT [FK_Pewo_Schedule_Created_By] FOREIGN KEY ([created_by])
        REFERENCES [dbo].[Corporate_User] ([id_User]),
    CONSTRAINT [FK_Pewo_Schedule_Updated_By] FOREIGN KEY ([updated_by])
        REFERENCES [dbo].[Corporate_User] ([id_User])
);
GO

CREATE INDEX [IX_Pewo_Schedule_Due]
    ON [dbo].[Pewo_Schedule] ([Status], [Next_Run_At])
    WHERE [is_Enabled] = 1;
GO

EXECUTE sp_addextendedproperty @name = N'MS_Description', @value = N'Auto incremented primary key column', @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Pewo_Schedule', @level2type = N'COLUMN', @level2name = N'id_Schedule';
GO
EXECUTE sp_addextendedproperty @name = N'MS_Description', @value = N'Cron expression defining run cadence e.g. 0 * * * * for hourly, or sentinel EVENT_PRIMED for externally-triggered-only schedules. varchar(100)', @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Pewo_Schedule', @level2type = N'COLUMN', @level2name = N'Cron_Expression';
GO
EXECUTE sp_addextendedproperty @name = N'MS_Description', @value = N'Next scheduled UTC run time. Due jobs query filters WHERE Next_Run_At <= GETUTCDATE()', @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Pewo_Schedule', @level2type = N'COLUMN', @level2name = N'Next_Run_At';
GO
EXECUTE sp_addextendedproperty @name = N'MS_Description', @value = N'Schedule status varchar(20): ACTIVE, SUSPENDED, DISABLED', @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Pewo_Schedule', @level2type = N'COLUMN', @level2name = N'Status';
GO