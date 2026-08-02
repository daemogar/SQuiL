Create Temp Table Returns_PgFlag (FlagID int4 Primary Key, IsActive boolean, RowGuid uuid, CreatedAt timestamp);
Insert Into Returns_PgFlag (FlagID, IsActive, RowGuid, CreatedAt) Select 1, true, '6f9619ff-8b86-d011-b42d-00c04fc964ff', '2026-07-27 13:45:00';
Select FlagID, IsActive, RowGuid, CreatedAt From Returns_PgFlag;
