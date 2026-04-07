using System.Diagnostics;

namespace SQuiL.Tests.VarcharMax;

public class VarcharMaxTests : BaseTest
{
	[Fact]
	public Task InputParameterTakesMaxStringConvertToTableArray()
	{
		Debugger.Launch();

		//--Declare	@Params_LongText table ([Error] varchar(max));

		var name = nameof(InputParameterTakesMaxStringConvertToTableArray);
		return TestHelper.Verify([TestHeader([name])], [$$"""
			--Name: {{name}}
			
			Declare	@Param_LongText varchar(max);
		
			Use [Database];
			
			Select 1;
			"""]);
	}
}
