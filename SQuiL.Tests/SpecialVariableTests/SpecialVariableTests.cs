namespace SQuiL.Tests;

using static Microsoft.CodeAnalysis.SourceGeneratorHelper;

public class SpecialVariableTests
{
	// @SuppressDebug and @AsOfDate parse without an SP1001 "must be prefixed" error.
	// Phase 2 (Task 5) emission: opt-in `bool Debug` + `bool SuppressDebug` plus a
	// nullable typed `AsOfDate` Request property (the @Debug command parameter is
	// gated with `!request.SuppressDebug`). @AsOfDate/@SuppressDebug command-parameter
	// emission itself lands in Task 6; here AsOfDate surfaces only as a Request property.
	[Fact]
	public Task SuppressDebugAndAsOfDateAreRecognizedSpecials()
		=> TestHelper.Verify([$$"""
			using Microsoft.Extensions.Configuration;
			using {{NamespaceName}};

			namespace TestCase;

			[{{QueryAttributeName}}(QueryFiles.AsOf)]
			public partial class AsOfDataContext(IConfiguration Configuration) : {{BaseDataContextClassName}}(Configuration)
			{
			}
			"""], ["""
			--Name: AsOf
			Declare @Debug bit = 1;
			Declare @SuppressDebug bit = 0;
			Declare @AsOfDate date = '2008-10-01';
			Declare @Return_Count int;
			Use MyDatabase;
			Set @Return_Count = (Select Count(*) From Logs Where CreatedOn <= @AsOfDate);
			Select @Return_Count;
			"""]);

	// SP0019: @SuppressDebug declared without @Debug is a build error. This snapshot
	// is final — accept it once it shows the SP0019 diagnostic. compileCheck is
	// disabled because this is an error-path fixture.
	[Fact]
	public Task SuppressDebugWithoutDebugIsAnError()
		=> TestHelper.Verify([$$"""
			using Microsoft.Extensions.Configuration;
			using {{NamespaceName}};

			namespace TestCase;

			[{{QueryAttributeName}}(QueryFiles.SuppressOnly)]
			public partial class SuppressOnlyDataContext(IConfiguration Configuration) : {{BaseDataContextClassName}}(Configuration)
			{
			}
			"""], ["""
			--Name: SuppressOnly
			Declare @SuppressDebug bit = 0;
			Declare @Return_Count int;
			Use MyDatabase;
			Select @Return_Count = 1;
			Select @Return_Count;
			"""], compileCheck: false);
}
