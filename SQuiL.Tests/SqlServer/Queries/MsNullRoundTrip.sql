Declare @Returns_MsRow table(RowID int Primary Key, Note varchar(50) null, Score int null);
Use [tempdb];
Insert Into @Returns_MsRow (RowID, Note, Score) Select 1, NULL, NULL;
Select RowID, Note, Score From @Returns_MsRow;
