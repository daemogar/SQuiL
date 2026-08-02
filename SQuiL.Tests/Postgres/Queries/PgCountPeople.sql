Create Temp Table Params_PgCounting (PersonID int4 Primary Key, Name text);
Create Temp Table Return_Total (Total int8);
Insert Into Return_Total (Total) Select Count(*) From Params_PgCounting;
Select Total From Return_Total;
