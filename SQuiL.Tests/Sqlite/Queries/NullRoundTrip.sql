Create Temp Table Returns_Row (RowID INTEGER Primary Key, Note TEXT null, Score INTEGER null);
Insert Into Returns_Row (RowID, Note, Score) Select 1, NULL, NULL;
Select RowID, Note, Score From Returns_Row;
