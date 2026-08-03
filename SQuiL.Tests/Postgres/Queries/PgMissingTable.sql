Create Temp Table Return_Total (Total int8);
Insert Into Return_Total (Total) Select Count(*) From NonExistentTable_XYZ;
Select Total From Return_Total;
