Declare @Params_MsCounting table(PersonID int Primary Key, Name varchar(50));
Declare @Return_Total bigint;
Use [tempdb];
Set @Return_Total = (Select Count_Big(*) From @Params_MsCounting);
Select @Return_Total;
