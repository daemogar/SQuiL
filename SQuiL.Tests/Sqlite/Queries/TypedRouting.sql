Create Temp Table Returns_Flag (FlagID INTEGER Primary Key, IsActive BOOLEAN, RowGuid GUID, CreatedAt DATETIME);
Insert Into Returns_Flag (FlagID, IsActive, RowGuid, CreatedAt) Select 1, 1, '6f9619ff-8b86-d011-b42d-00c04fc964ff', '2026-07-27 13:45:00';
Select FlagID, IsActive, RowGuid, CreatedAt From Returns_Flag;
