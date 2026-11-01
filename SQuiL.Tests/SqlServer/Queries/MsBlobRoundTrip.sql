Declare @Params_MsDoc table(DocID int Primary Key, Payload varbinary(max));
Declare @Returns_MsStored table(DocID int Primary Key, Payload varbinary(max));
Use [tempdb];
Insert Into @Returns_MsStored (DocID, Payload) Select DocID, Payload From @Params_MsDoc;
Select DocID, Payload From @Returns_MsStored;
