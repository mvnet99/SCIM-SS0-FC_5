CREATE OR ALTER PROCEDURE dbo.usp_Pewo_InsertLog
    @id_WorkflowRun INT,
    @id_Customer    INT = NULL,
    @Customer_Name  NVARCHAR(200) = NULL,
    @Step_Kind      NVARCHAR(50) = NULL,
    @Log_Level      NVARCHAR(10) = 'INFO',
    @Message        NVARCHAR(2000),
    @Event_Context  NVARCHAR(500) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    INSERT INTO dbo.Pewo_WorkflowRunLog
        (id_WorkflowRun, id_Customer, Customer_Name, Step_Kind,
         Log_Level, Message, Event_Context, logged_date, created_by)
    VALUES
        (@id_WorkflowRun, @id_Customer, @Customer_Name, @Step_Kind,
         @Log_Level, @Message, @Event_Context, GETUTCDATE(), 1);
END
