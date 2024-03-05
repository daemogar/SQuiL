
Declare @Param_AsOfDate date = Null;

Set @Param_AsOfDate = '2024-05-01';

Declare @Returns_Terms table(TermCode varchar(10));

Use BIWarehouse;

If @Param_AsOfDate Is Null Set @Param_AsOfDate = GetDate();

Insert Into @Returns_Terms
Select		t.Term
From		pub.Terms t
Where		@Param_AsOfDate Between t.RegStartDate And IsNull(t.GradesDueDate, DateAdd(week, 1, t.EndDate))

Select * From @Returns_Terms;
