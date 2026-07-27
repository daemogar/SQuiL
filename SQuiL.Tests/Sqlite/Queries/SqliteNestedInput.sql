Create Temp Table Param_Cart (CartID INTEGER Primary Key, ShopperName TEXT);
Create Temp Table Params_Item (ItemID INTEGER Primary Key, CartID INTEGER, Product TEXT);
Create Temp Table Returns_CartLine (ShopperName TEXT, Product TEXT);
Insert Into Returns_CartLine (ShopperName, Product) Select c.ShopperName, i.Product From Param_Cart c Join Params_Item i On i.CartID = c.CartID;
Select ShopperName, Product From Returns_CartLine;
