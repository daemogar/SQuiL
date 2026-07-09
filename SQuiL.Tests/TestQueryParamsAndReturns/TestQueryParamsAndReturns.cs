namespace SQuiL.Tests.TestQueryParamsAndReturns;

public class TestQueryParamsAndReturns : BaseTest
{
    // NOTE: the two outputs must have DISTINCT column signatures. Under signature-based
    // result routing (TODO #7) two outputs with an identical ordered shape would collide on
    // the same reader switch case (a build error, formalized as SP0030). This test's purpose
    // is nullable columns in returned object/table shapes, so we keep the shapes distinct
    // (StudentID/ParentID) while still exercising `Null` columns on both sides.
    //
    // REGRESSION GUARD (TODO #19): the `Null` value-type columns (Age, YearsOld) must emit
    // `? default(int?) :`, NOT `? default! :`. A bare default! reads a NULL int back as 0
    // (best-common-type collapses the ternary to int). Do not "simplify" the snapshot back
    // to default! — that reintroduces the null-value-type read bug.
    [Fact]
    public Task ObjectPropertyNullable() => TestQueryParamsAndReturns($"""
		Declare @Return_Student table(StudentID int, FirstName varchar(100) Null, LastName varchar(100), Age int Null);
		Declare @Returns_Parents table(ParentID int, MiddleName varchar(100) Null, Surname varchar(100), YearsOld int Null);
		""");
}
