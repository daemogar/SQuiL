Create Temp Table Debug (Value int4);
Create Temp Table Param_PgPersonID (PersonID int4);
Create Temp Table Param_PgLang (Lang text);
Create Temp Table Param_PgDayEnd (DayEnd timestamptz);
Create Temp Table Params_PgSlot (SlotID int4 Primary Key, StartsAt timestamptz);
Create Temp Table Return_PgEcho (PersonID int4, Lang text, DayEnd timestamptz, SlotsBefore int4, DebugRows int4);
Insert Into Param_PgPersonID (PersonID) Values (1);
Insert Into Param_PgLang (Lang) Values ('en');
Insert Into Param_PgDayEnd (DayEnd) Values (now());
Insert Into Return_PgEcho (PersonID, Lang, DayEnd, SlotsBefore, DebugRows)
Select (Select PersonID From Param_PgPersonID),
	(Select Lang From Param_PgLang),
	(Select DayEnd From Param_PgDayEnd),
	(Select Count(*) From Params_PgSlot Where StartsAt < (Select DayEnd From Param_PgDayEnd)),
	(Select Count(*) From Debug);
Select PersonID, Lang, DayEnd, SlotsBefore, DebugRows From Return_PgEcho;
