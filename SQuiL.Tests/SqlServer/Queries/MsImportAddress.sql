Declare @Param_MsAddress table(Street varchar(100), City varchar(50));
Declare @Return_MsAddress table(Street varchar(100), City varchar(50));
Use [tempdb];
Insert Into @Return_MsAddress (Street, City) Select Street, City From @Param_MsAddress;
Select Street, City From @Return_MsAddress;
