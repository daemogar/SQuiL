Create Temp Table Debug (Value INTEGER);
Create Temp Table Param_PersonID (PersonID INTEGER);
Create Temp Table Param_Lang (Lang TEXT);
Create Temp Table Return_Echo (PersonID INTEGER, Lang TEXT, DebugRows INTEGER);
Insert Into Param_PersonID (PersonID) Values (1);
Insert Into Param_Lang (Lang) Values ('en');
Insert Into Return_Echo (PersonID, Lang, DebugRows)
Select (Select PersonID From Param_PersonID), (Select Lang From Param_Lang), (Select Count(*) From Debug);
Select PersonID, Lang, DebugRows From Return_Echo;
