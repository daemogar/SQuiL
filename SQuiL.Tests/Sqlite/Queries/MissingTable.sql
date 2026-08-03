Create Temp Table Return_Total (Total INTEGER);
Insert Into Return_Total (Total) Select Count(*) From NonExistentTable_XYZ;
Select Total From Return_Total;
