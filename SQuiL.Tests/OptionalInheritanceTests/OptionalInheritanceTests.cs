namespace SQuiL.Tests;

using static Microsoft.CodeAnalysis.SourceGeneratorHelper;

public class OptionalInheritanceTests
{
	// Zero-config context: no base, no constructor. The generator emits
	// <Namespace>.<Ctx>.Constructor.g.cs supplying ': SQuiLBaseDataContext' + an IConfiguration ctor.
	[Fact]
	public Task ZeroConfigContextEmitsConstructor()
		=> TestHelper.Verify([$$"""
			using {{NamespaceName}};

			namespace TestCase;

			[{{QueryAttributeName}}(QueryFiles.ZeroConfig)]
			public partial class ZeroConfigDataContext
			{
			}
			"""], ["""
			--Name: ZeroConfig
			Declare @Param_PersonID varchar(10);
			Declare @Return_Count int;
			Use MyDatabase;
			Set @Return_Count = (Select Count(*) From People Where PersonID = @Param_PersonID);
			Select @Return_Count;
			"""]);

	// Opt-out: the developer declares a constructor, so NO constructor file is emitted.
	[Fact]
	public Task DeclaredConstructorSuppressesGeneratedConstructor()
		=> TestHelper.Verify([$$"""
			using Microsoft.Extensions.Configuration;
			using {{NamespaceName}};

			namespace TestCase;

			[{{QueryAttributeName}}(QueryFiles.CustomCtor)]
			public partial class CustomCtorDataContext
			{
				public CustomCtorDataContext(IConfiguration configuration) : base(configuration) { }
			}
			"""], ["""
			--Name: CustomCtor
			Declare @Return_Count int;
			Use MyDatabase;
			Select @Return_Count = 1;
			Select @Return_Count;
			"""]);

	// Opt-out via primary constructor (the canonical explicit authoring form): the
	// generator must NOT emit a constructor file when the context declares a primary ctor.
	[Fact]
	public Task PrimaryConstructorSuppressesGeneratedConstructor()
		=> TestHelper.Verify([$$"""
			using Microsoft.Extensions.Configuration;
			using {{NamespaceName}};

			namespace TestCase;

			[{{QueryAttributeName}}(QueryFiles.PrimaryCtor)]
			public partial class PrimaryCtorDataContext(IConfiguration Configuration) : {{BaseDataContextClassName}}(Configuration)
			{
			}
			"""], ["""
			--Name: PrimaryCtor
			Declare @Return_Count int;
			Use MyDatabase;
			Select @Return_Count = 1;
			Select @Return_Count;
			"""]);
}
