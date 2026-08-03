Create Temp Table Params_PgRoster (PersonID int4 Primary Key, Name text);
Create Temp Table Returns_PgEchoed (PersonID int4 Primary Key, Name text);
Create Temp Table Return_Total (Total int8);
Insert Into Returns_PgEchoed (PersonID, Name) Select PersonID, Name From Params_PgRoster;
Insert Into Return_Total (Total) Select Count(*) From Params_PgRoster;
Select PersonID, Name From Returns_PgEchoed;
Select Total From Return_Total;
