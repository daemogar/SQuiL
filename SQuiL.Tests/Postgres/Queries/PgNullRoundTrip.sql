Create Temp Table Returns_PgRow (RowID int4 Primary Key, Note text null, Score int4 null);
Insert Into Returns_PgRow (RowID, Note, Score) Select 1, NULL, NULL;
Select RowID, Note, Score From Returns_PgRow;
