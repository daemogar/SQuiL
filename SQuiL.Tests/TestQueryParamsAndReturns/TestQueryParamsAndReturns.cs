namespace SQuiL.Tests.TestQueryParamsAndReturns;

public class TestQueryParamsAndReturns : BaseTest
{
    [Fact]
    public Task ObjectPropertyNullable() => TestQueryParamsAndReturns($"""
		Declare @Return_Student table(ID int, FirstName varchar(100) Null, LastName varchar(100), Age int Null);
		Declare @Returns_Parents table(ID int, FirstName varchar(100) Null, LastName varchar(100), Age int Null);
		""");
}
