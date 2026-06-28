CREATE OR ALTER PROCEDURE dbo.usp_Pewo_AdvanceSchedule
    @id_Schedule  INT,
    @Next_Run_At  DATETIME,
    @Last_Run_Id  INT,
    @Last_Status  NVARCHAR(20)
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.Pewo_Schedule
    SET    Next_Run_At        = @Next_Run_At,
           Last_Run_At        = GETUTCDATE(),
           Last_Run_Id        = @Last_Run_Id,
           Last_Status        = @Last_Status,
           last_updated_date  = GETUTCDATE()
    WHERE  id_Schedule = @id_Schedule;
END
