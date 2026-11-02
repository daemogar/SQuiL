Declare @Return_Database nvarchar(128) = null;
Use [tempdb];
Set @Return_Database = Db_Name();
Select @Return_Database;
