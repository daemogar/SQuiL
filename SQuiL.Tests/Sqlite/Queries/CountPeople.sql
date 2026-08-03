Create Temp Table Params_Counting (PersonID INTEGER Primary Key, Name TEXT);
Create Temp Table Return_Total (Total INTEGER);
Insert Into Return_Total (Total) Select Count(*) From Params_Counting;
Select Total From Return_Total;
