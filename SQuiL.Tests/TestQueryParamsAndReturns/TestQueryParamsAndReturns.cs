namespace SQuiL.Tests.TestQueryParamsAndReturns;

public class TestQueryParamsAndReturns : BaseTest
{
    // NOTE: the two outputs must have DISTINCT column signatures. Under signature-based
    // result routing (TODO #7) two outputs with an identical ordered shape would collide on
    // the same reader switch case (a build error, formalized as SP0030). This test's purpose
    // is nullable columns in returned object/table shapes, so we keep the shapes distinct
    // (StudentID/ParentID) while still exercising `Null` columns on both sides.
    [Fact]
    public Task ObjectPropertyNullable() => TestQueryParamsAndReturns($"""
		Declare @Return_Student table(StudentID int, FirstName varchar(100) Null, LastName varchar(100), Age int Null);
		Declare @Returns_Parents table(ParentID int, MiddleName varchar(100) Null, Surname varchar(100), YearsOld int Null);
		""");
}
