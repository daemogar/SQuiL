using System.Diagnostics;

namespace SQuiL.Tests.TableNameMerge;

public class TableNameMergeTests : BaseTest
{
	[Fact]
	public Task TwoQueriesWithMismatchedShapes()
	{
		var name = nameof(TwoQueriesWithMismatchedShapes);
		return TestHelper.Verify([TestHeader([$"{name}1", $"{name}2"])], [$$"""
			--Name: {{name}}1

			Declare @Returns_Questions table(
				Number int,
				[Message] varchar(max)
			);

			Use [Database];

			Select * From @Returns_Questions;
			""", $$"""
			--Name: {{name}}2

			Declare @Returns_Questions table(
				Number int,
				Topic varchar(50)
			);

			Use [Database];

			Select * From @Returns_Questions;
			"""]);
	}

	[Fact]
	public Task MappedTablesWithMismatchedShapes()
	{
		var name = nameof(MappedTablesWithMismatchedShapes);
		return TestHelper.Verify([$$"""
			{{TestHeader([$"{name}1", $"{name}2"])}}

			[SQuiLTable(TableType.People)]
			[SQuiLTable(TableType.Persons)]
			public partial record SharedTable {}
			"""], [$$"""
			--Name: {{name}}1

			Declare @Returns_People table(
				PersonID int,
				FirstName varchar(100)
			);

			Use [Database];

			Select * From @Returns_People;
			""", $$"""
			--Name: {{name}}2

			Declare @Returns_Persons table(
				PersonID int,
				LastName varchar(100)
			);

			Use [Database];

			Select * From @Returns_Persons;
			"""]);
	}

	[Fact]
	public Task SameFileShapeMismatchPersonTable()
	{
		var name = nameof(SameFileShapeMismatchPersonTable);
		return TestHelper.Verify([TestHeader([name])], [$$"""
			--Name: {{name}}

			Declare @Returns_Person table(PersonID int, FullName varchar(100));
			Declare @Return_Person table(PersonID int, Age int);

			Use [Database];

			Select * From @Returns_Person;
			Select * From @Return_Person;
			"""]);
	}

	[Fact]
	public Task TwoQueriesWithSameReference()
	{
		//Debugger.Launch();

		var name = nameof(TwoQueriesWithSameReference);
		return TestHelper.Verify([TestHeader([$"{name}1", $"{name}2"])], [$$"""
			--Name: {{name}}1
			
			Declare @Returns_Questions table(
				Number int,
				[Message] varchar(max)
			);

			Use [Database];

			Select * From @Returns_Questions;
			""", $$"""
			--Name: {{name}}2
			
			Declare @Param_Question table(
				Number int,
				[Message] varchar(max)
			);
			
			Use [Database];
			
			Select * From @Param_Question;
			"""]);
	}
}
