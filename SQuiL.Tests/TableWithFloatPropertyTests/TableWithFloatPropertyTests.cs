namespace SQuiL.Tests;

public class TableWithFloatPropertyTests : BaseTest
{
	[Fact]
	public Task DoubleNumberTest()
	{
		//Debugger.Launch();

		var name = nameof(DoubleNumberTest);
		return TestHelper.Verify([TestHeader([name])], [$$"""
			--Name: {{name}}
			Declare @Returns_Answers table(Response float);
			Use [Database];
			"""]);
	}
}
