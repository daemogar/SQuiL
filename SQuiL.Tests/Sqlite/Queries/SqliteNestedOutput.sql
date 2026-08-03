Create Temp Table Returns_Order (OrderID INTEGER Primary Key, CustomerName TEXT);
Create Temp Table Returns_Line (LineID INTEGER Primary Key, OrderID INTEGER, Product TEXT, Qty INTEGER);
Insert Into Returns_Order (OrderID, CustomerName) Values (1, 'Ada'), (2, 'Alan');
Insert Into Returns_Line (LineID, OrderID, Product, Qty) Values (10, 1, 'Widget', 3), (11, 1, 'Gadget', 1), (12, 2, 'Gizmo', 5);
Select OrderID, CustomerName From Returns_Order;
Select LineID, OrderID, Product, Qty From Returns_Line;
