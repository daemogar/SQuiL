Create Temp Table Params_PgDoc (DocID int4 Primary Key, Payload bytea);
Create Temp Table Returns_PgStored (DocID int4 Primary Key, Payload bytea);
Insert Into Returns_PgStored (DocID, Payload) Select DocID, Payload From Params_PgDoc;
Select DocID, Payload From Returns_PgStored;
