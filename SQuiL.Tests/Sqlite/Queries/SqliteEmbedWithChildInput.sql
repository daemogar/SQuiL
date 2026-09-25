Create Temp Table Params_Structure (Title TEXT not null, ContactID INTEGER not null);
Create Temp Table Params_Contact (ContactID INTEGER not null Primary Key, Name TEXT not null);
Create Temp Table Params_Phone (PhoneID INTEGER not null Primary Key, ContactID INTEGER not null, Number TEXT not null);
Create Temp Table Returns_PhoneLine (Title TEXT not null, Number TEXT not null);
Insert Into Returns_PhoneLine (Title, Number) Select s.Title, p.Number From Params_Structure s Join Params_Phone p On p.ContactID = s.ContactID;
Select Title, Number From Returns_PhoneLine;
