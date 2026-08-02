Create Temp Table Param_Address (Street text, City text);
Create Temp Table Return_Address (Street text, City text);
Insert Into Return_Address (Street, City) Select Street, City From Param_Address;
Select Street, City From Return_Address;
