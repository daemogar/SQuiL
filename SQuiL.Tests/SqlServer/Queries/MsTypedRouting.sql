Declare @Returns_MsFlag table(FlagID int Primary Key, IsActive bit, RowGuid uniqueidentifier, CreatedAt datetime2, Amount decimal(18,2), Stamp datetimeoffset);
Use [tempdb];
Insert Into @Returns_MsFlag (FlagID, IsActive, RowGuid, CreatedAt, Amount, Stamp)
Select 1, 1, '6f9619ff-8b86-d011-b42d-00c04fc964ff', '2026-07-27T13:45:00', 12.34, '2026-07-27T13:45:00-05:00';
Select FlagID, IsActive, RowGuid, CreatedAt, Amount, Stamp From @Returns_MsFlag;
