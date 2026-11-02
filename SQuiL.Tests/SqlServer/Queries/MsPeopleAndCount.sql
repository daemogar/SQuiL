Declare @Params_MsRoster table(PersonID int Primary Key, Name varchar(50));
Declare @Returns_MsEchoed table(PersonID int Primary Key, Name varchar(50));
Declare @Return_Total bigint;
Use [tempdb];
Insert Into @Returns_MsEchoed (PersonID, Name) Select PersonID, Name From @Params_MsRoster;
Set @Return_Total = (Select Count_Big(*) From @Params_MsRoster);
Select PersonID, Name From @Returns_MsEchoed;
Select @Return_Total;
