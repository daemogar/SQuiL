Create Temp Table Params_PgPerson (PersonID int4 Primary Key, Name text, Age int4);
Create Temp Table Returns_PgImported (PersonID int4 Primary Key, Name text, Age int4);
Insert Into Returns_PgImported (PersonID, Name, Age) Select PersonID, Name, Age From Params_PgPerson;
Select PersonID, Name, Age From Returns_PgImported;
