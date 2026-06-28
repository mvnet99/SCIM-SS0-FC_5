-- =============================================
-- PEWO: WorkflowStepDef
-- Data-driven step template per workflow type.
-- Disable a step: set is_Active = 0
-- Add a step: insert a new row
-- Supported Step_Kinds are validated via Foreign Key lookup against Pewo_StepKind.
-- =============================================
CREATE TABLE [dbo].[Pewo_WorkflowStepDef] (
    [id_WorkflowStepDef]       INT            IDENTITY (1, 1) NOT NULL,
    [id_CustomerWorkflowType]  INT            NOT NULL,
    [Step_Order]               SMALLINT       NOT NULL,
    [Step_Kind]                VARCHAR (50)   NOT NULL,
    [Step_Name]                NVARCHAR (100) NOT NULL,
    [Config]                   NVARCHAR (MAX) NULL,
    [Max_Attempts]             SMALLINT       CONSTRAINT [df_Pewo_WorkflowStepDef_Max_Attempts] DEFAULT ((3)) NOT NULL,
    [Backoff_Seconds]          INT            CONSTRAINT [df_Pewo_WorkflowStepDef_Backoff_Seconds] DEFAULT ((30)) NOT NULL,
    [is_Active]                BIT            CONSTRAINT [df_Pewo_WorkflowStepDef_is_Active] DEFAULT ((1)) NOT NULL,
    [created_date]             DATETIME       NULL,
    [last_updated_date]        DATETIME       NULL,
    [created_by]               INT            NULL,
    [updated_by]               INT            NULL,
    CONSTRAINT [PK_Pewo_WorkflowStepDef] PRIMARY KEY CLUSTERED ([id_WorkflowStepDef] ASC),
    CONSTRAINT [UQ_Pewo_WorkflowStepDef_Order] UNIQUE ([id_CustomerWorkflowType], [Step_Order]),
    CONSTRAINT [FK_Pewo_WorkflowStepDef_CustomerWorkflowType] FOREIGN KEY ([id_CustomerWorkflowType])
        REFERENCES [dbo].[Pewo_CustomerWorkflowType] ([id_CustomerWorkflowType]),
    CONSTRAINT [FK_Pewo_WorkflowStepDef_StepKind] FOREIGN KEY ([Step_Kind])
        REFERENCES [dbo].[Pewo_StepKind] ([Step_Kind]),
    CONSTRAINT [FK_Pewo_WorkflowStepDef_Created_By] FOREIGN KEY ([created_by])
        REFERENCES [dbo].[Corporate_User] ([id_User]),
    CONSTRAINT [FK_Pewo_WorkflowStepDef_Updated_By] FOREIGN KEY ([updated_by])
        REFERENCES [dbo].[Corporate_User] ([id_User])
);
GO

EXECUTE sp_addextendedproperty @name = N'MS_Description', @value = N'Auto incremented primary key column', @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Pewo_WorkflowStepDef', @level2type = N'COLUMN', @level2name = N'id_WorkflowStepDef';
GO
EXECUTE sp_addextendedproperty @name = N'MS_Description', @value = N'Foreign key to Customer_WorkflowType table', @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Pewo_WorkflowStepDef', @level2type = N'COLUMN', @level2name = N'id_CustomerWorkflowType';
GO
EXECUTE sp_addextendedproperty @name = N'MS_Description', @value = N'Execution order of this step within the workflow, smallint', @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Pewo_WorkflowStepDef', @level2type = N'COLUMN', @level2name = N'Step_Order';
GO
EXECUTE sp_addextendedproperty @name = N'MS_Description', @value = N'Type of step handler to invoke. Foreign key referencing Pewo_StepKind table.', @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Pewo_WorkflowStepDef', @level2type = N'COLUMN', @level2name = N'Step_Kind';
GO
EXECUTE sp_addextendedproperty @name = N'MS_Description', @value = N'JSON configuration passed to the step handler at runtime nvarchar(max)', @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Pewo_WorkflowStepDef', @level2type = N'COLUMN', @level2name = N'Config';
GO
EXECUTE sp_addextendedproperty @name = N'MS_Description', @value = N'Set to 0 to disable this step without code change, bit default 1', @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Pewo_WorkflowStepDef', @level2type = N'COLUMN', @level2name = N'is_Active';
GO