Create Temp Table Params_Widget (WidgetID INTEGER Primary Key, Name TEXT);
Insert Into Widgets (WidgetID, Name) Select WidgetID, Name From Params_Widget;
Insert Into NonExistentTable_XYZ (Bogus) Values (1);
