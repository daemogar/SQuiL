Create Temp Table Params_Office (Title TEXT not null, AgentID INTEGER not null);
Create Temp Table Params_Agent (AgentID INTEGER not null Primary Key, Name TEXT not null, SiteID INTEGER not null);
Create Temp Table Params_Site (SiteID INTEGER not null Primary Key, Street TEXT not null);
Create Temp Table Returns_OfficeStreet (Title TEXT not null, Street TEXT not null);
Insert Into Returns_OfficeStreet (Title, Street) Select o.Title, s.Street From Params_Office o Join Params_Agent g On g.AgentID = o.AgentID Join Params_Site s On s.SiteID = g.SiteID;
Select Title, Street From Returns_OfficeStreet;
