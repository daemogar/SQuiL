namespace SQuiL.Tests;

using System.Runtime.CompilerServices;

using static Microsoft.CodeAnalysis.SourceGeneratorHelper;

public class ParamShardingTests
{
	private static string Header([CallerMemberName] string name = default!) => $$"""
		using Microsoft.Extensions.Configuration;
		using {{NamespaceName}};

		namespace TestCase;

		[{{QueryAttributeName}}(QueryFiles.{{name}})]
		public partial class {{name}}DataContext(IConfiguration Configuration) : {{BaseDataContextClassName}}(Configuration)
		{
		}
		""";

	// Large multi-column list: proves the rows×cols / 2100-param / 1000-row ceiling is gone
	// — the emitted fill is one nvarchar(max) JSON parameter regardless of row count.
	[Fact]
	public Task LargeListUsesSingleJsonParameter()
	{
		var name = nameof(LargeListUsesSingleJsonParameter);
		return TestHelper.Verify([Header()], [$$"""
			--Name: {{name}}
			Declare @Params_Rows table(RowID int, Amount decimal(18,2), Note varchar(50));
			Use [Database];
			Select 1;
			"""]);
	}

	// One row per supported scalar type → exercises every WITH-clause type mapping.
	// NOTE: bigint is not yet first-class in the tokenizer (SP1001 parse error);
	// dropped per TODO #9 (type-map completeness). In-scope set: int, bit,
	// decimal, float, varchar, date, datetime2, datetimeoffset, uniqueidentifier.
	[Fact]
	public Task PerTypeFidelity()
	{
		var name = nameof(PerTypeFidelity);
		return TestHelper.Verify([Header()], [$$"""
			--Name: {{name}}
			Declare @Params_All table(
				Id int,
				Flag bit,
				Amount decimal(18,2),
				Ratio float,
				Name varchar(100),
				WhenDate date,
				WhenStamp datetime2,
				WhenOffset datetimeoffset,
				Key uniqueidentifier);
			Use [Database];
			Select 1;
			"""]);
	}

	// Binary columns: bare-hex JSON + CONVERT(varbinary(...), col, 2) in the WITH/SELECT.
	[Fact]
	public Task BinaryListUsesHexConvert()
	{
		var name = nameof(BinaryListUsesHexConvert);
		return TestHelper.Verify([Header()], [$$"""
			--Name: {{name}}
			Declare @Params_Blobs table(BlobID int, Fixed binary(10), Var varbinary(max));
			Use [Database];
			Select 1;
			"""]);
	}

	// Single-object input → one-element JSON array, same OPENJSON fill path.
	[Fact]
	public Task SingleObjectUsesOneElementArray()
	{
		var name = nameof(SingleObjectUsesOneElementArray);
		return TestHelper.Verify([Header()], [$$"""
			--Name: {{name}}
			Declare @Param_Row table(RowID int, Name varchar(50));
			Use [Database];
			Select 1;
			"""]);
	}

	// Nullable string column with explicit null marker → JSON null flows to SQL NULL,
	// no length guard, no throw.
	[Fact]
	public Task NullableColumnNoGuard()
	{
		var name = nameof(NullableColumnNoGuard);
		return TestHelper.Verify([Header()], [$$"""
			--Name: {{name}}
			Declare @Params_People table(PersonID int, NickName varchar(50) null);
			Use [Database];
			Select 1;
			"""]);
	}
}
