namespace SquilParser.Tests;

using System.Runtime.CompilerServices;

using static Microsoft.CodeAnalysis.SourceGeneratorHelper;

public class BasicIODeclareTests
{
	private static string TestHeader(
		IEnumerable<string> attributes = default!,
		Func<string, string> callback = default!,
		[CallerMemberName] string name = default!)
	{
		attributes ??= [name];
		callback ??= p => $$"""
			[{{QueryAttributeName}}(QueryFiles.{{p}})]
			""";

		return $$"""
			using Microsoft.Extensions.Configuration;
			using {{NamespaceName}};
		
			namespace TestCase;
		
			{{string.Join("", attributes.Select(callback))}}
			public partial class {{name}}DataContext(IConfiguration Configuration) : {{BaseDataContextClassName}}(Configuration)
			{
			}
			""";
	}

	[Fact]
	public Task Input2Variable()
	{
		var name = nameof(Input2Variable);
		return TestHelper.Verify([TestHeader([name])], [$$"""
			--Name: {{name}}			
			Declare	@Param_Object table(ObjectID int, IsMale bit, FirstName varchar(100));
			Use [Database];
			Select 1;
			"""]);
	}

	[Fact]
	public Task FullVariable()
	{
		var name = nameof(FullVariable);
		return TestHelper.Verify([TestHeader([name])], [$$"""
			--Name: {{name}}
			Declare	@Param_Scaler int;
			
			Declare	@Param_Object table(ObjectID int, IsMale bit, FirstName varchar(100));
			
			Declare	@Params_Table table(TableID int, IsFemale bit, LastName varchar(100));
			
			Declare	@Return_Scaler int;
			
			Declare	@Return_Object table(ObjectID int, IsNeither bit, PreferredName varchar(100));
			
			Declare	@Returns_Table table(TableID int, IsBoth bit, NickName varchar(100));

			Use [Database];
			Select 1;
			"""]);
	}
}
