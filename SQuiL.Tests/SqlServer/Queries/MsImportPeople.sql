Declare @Params_MsPerson table(PersonID int Primary Key, Name varchar(50), Age int);
Declare @Returns_MsImported table(PersonID int Primary Key, Name varchar(50), Age int);
Use [tempdb];
Insert Into @Returns_MsImported (PersonID, Name, Age) Select PersonID, Name, Age From @Params_MsPerson;
Select PersonID, Name, Age From @Returns_MsImported;
