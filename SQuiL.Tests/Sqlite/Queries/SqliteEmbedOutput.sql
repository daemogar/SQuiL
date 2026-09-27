Create Temp Table Returns_Building (Title TEXT not null, OwnerID INTEGER not null);
Create Temp Table Returns_Owner (OwnerID INTEGER not null Primary Key, Name TEXT not null);
Insert Into Returns_Building (Title, OwnerID) Values ('A', 1), ('B', 2), ('C', 1);
Insert Into Returns_Owner (OwnerID, Name) Values (1, 'Ada'), (2, 'Alan');
Select Title, OwnerID From Returns_Building;
Select OwnerID, Name From Returns_Owner;
