Create Temp Table Params_Person (PersonID INTEGER Primary Key, Name TEXT, Age INTEGER);
Create Temp Table Returns_Imported (PersonID INTEGER Primary Key, Name TEXT, Age INTEGER);
Insert Into Returns_Imported (PersonID, Name, Age) Select PersonID, Name, Age From Params_Person;
Select PersonID, Name, Age From Returns_Imported;
