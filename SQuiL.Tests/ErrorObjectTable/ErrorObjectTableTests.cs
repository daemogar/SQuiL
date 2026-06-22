namespace SQuiL.Tests.ErrorObjectTable;

public class ErrorObjectTableTests : BaseTest
{
	[Fact]
	public Task ReturnErrorTableEmitsNormally()
	{
		// "Error"/"Errors" are no longer reserved names. A prefixed
		// @Return_Error is an ordinary output object and must emit a normal
		// record + response property + a Return_Error__ switch arm — it is no
		// longer swallowed by the (removed) @Error/@Errors mechanism.
		var name = nameof(ReturnErrorTableEmitsNormally);
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