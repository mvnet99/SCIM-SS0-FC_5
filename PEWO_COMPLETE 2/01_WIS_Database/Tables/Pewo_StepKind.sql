-- =============================================
-- PEWO: StepKind
-- Master lookup table for workflow step types.
-- Enables data-driven architecture where adding a new step kind is a pure data INSERT.
-- =============================================
CREATE TABLE [dbo].[Pewo_StepKind] (
    [Step_Kind] VARCHAR (50) NOT NULL,
    CONSTRAINT [PK_Pewo_StepKind] PRIMARY KEY CLUSTERED ([Step_Kind] ASC)
);
GO

EXECUTE sp_addextendedproperty @name = N'MS_Description', @value = N'Unique string identifier and primary key for a step handler type.', @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Pewo_StepKind', @level2type = N'COLUMN', @level2name = N'Step_Kind';
GO