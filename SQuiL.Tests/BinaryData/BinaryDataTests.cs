using static Microsoft.CodeAnalysis.SourceGeneratorHelper;

namespace SQuiL.Tests.BinaryData;

public class BinaryDataTests : BaseTest
{
	[Fact]
	public Task BinaryNullabilityFollowsMarker()
		=> TestHelper.Verify([$$"""
			using {{NamespaceName}};

			namespace TestCase;

			[{{QueryAttributeName}}(QueryFiles.Q)]
			public partial class C
			{
			}
			"""], ["""
			--Name: Q
			Declare @Param_Blob varbinary(max);
			Declare @Param_BlobNull varbinary(max) null;
			Use MyDb;
			Select 1;
			"""]);

	[Fact]
	public Task BinaryDataParameter()
	{
		//Debugger.Launch();

		var name = nameof(BinaryDataParameter);
		return TestHelper.Verify([TestHeader([name])], [$$"""
			--Name: {{name}}
			Declare @Param_BinaryDataField binary(10);
			
			Declare @Param_VarBinaryDataField varbinary(max);

			Declare @Returns_BinaryTable table(DataBinary binary(10), DataVarBinary varbinary(max));
			
			Use [Database];

			Select * From @Returns_BinaryTable
			"""]);
	}
}
