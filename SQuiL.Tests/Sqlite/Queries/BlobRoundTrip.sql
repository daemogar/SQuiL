Create Temp Table Params_Doc (DocID INTEGER Primary Key, Payload BLOB);
Create Temp Table Returns_Stored (DocID INTEGER Primary Key, Payload BLOB);
Insert Into Returns_Stored (DocID, Payload) Select DocID, Payload From Params_Doc;
Select DocID, Payload From Returns_Stored;
