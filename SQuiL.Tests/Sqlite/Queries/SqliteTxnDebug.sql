Create Temp Table Debug (Value INTEGER);
Create Temp Table Params_Widget (WidgetID INTEGER Primary Key, Name TEXT);
Create Temp Table Return_Inserted (Inserted INTEGER);
Insert Into Widgets (WidgetID, Name) Select WidgetID, Name From Params_Widget;
Insert Into Return_Inserted (Inserted) Select Count(*) From Params_Widget;
Select Inserted From Return_Inserted;
