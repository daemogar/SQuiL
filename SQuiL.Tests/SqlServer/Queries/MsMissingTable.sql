Declare @Return_Total bigint;
Use [tempdb];
Set @Return_Total = (Select Count_Big(*) From dbo.NonExistentTable_XYZ);
Select @Return_Total;
