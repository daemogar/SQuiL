Create Temp Table Params_Roster (PersonID INTEGER Primary Key, Name TEXT);
Create Temp Table Returns_Echoed (PersonID INTEGER Primary Key, Name TEXT);
Create Temp Table Return_Total (Total INTEGER);
Insert Into Returns_Echoed (PersonID, Name) Select PersonID, Name From Params_Roster;
Insert Into Return_Total (Total) Select Count(*) From Params_Roster;
Select PersonID, Name From Returns_Echoed;
Select Total From Return_Total;
