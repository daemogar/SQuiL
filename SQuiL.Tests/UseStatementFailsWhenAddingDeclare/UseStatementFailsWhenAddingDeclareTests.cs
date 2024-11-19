namespace SQuiL.Tests.UseStatementFailsWhenAddingDeclare;

public class UseStatementFailsWhenAddingDeclareTests : BaseTest
{
	[Fact]
	public Task UseStatementFailsWhenAddingDeclareTest() => TestQueryParamsAndReturns($$"""
		Declare @Returns_ExtendedCourses table(
			ProfessorID varchar(10),
			Username varchar(1000)
		);

		Use BIWarehouse;
		""");
}
