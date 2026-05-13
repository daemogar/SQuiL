namespace SQuiL.Tests.ErrorObjectTable;

public class ErrorObjectTableTests : BaseTest
{
	[Fact]
	public Task MakeSureErrorTableObjectDoesNotEmit()
	{
		//Debugger.Launch();

		//--Declare	@Params_LongText table ([Error] varchar(max));

		var name = nameof(MakeSureErrorTableObjectDoesNotEmit);
		return TestHelper.Verify([TestHeader([name])], [$$"""
			--Name: {{name}}
			
			Declare @Return_Error table(
				[Number] int,
				[Severity] int,
				[State] int,
				[Line] int,
				[Procedure] varchar(max),
				[Message] varchar(max));
		
			Use [Database];
			
			Select * From Return_Error;

			"""]);
	}
}
