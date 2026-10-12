using SQuiL.Generator;

namespace SQuiL.Tests;

/// <summary>Shape of the generated <c>SQuiLExtensions.g.cs</c> (TODO #30).</summary>
public class SQuiLDependencyInjectionEmitterTests
{
	private static readonly SQuiLRegistrationReference LibA = new(
		"LibA", "global::Microsoft.Extensions.DependencyInjection.SQuiLLibAExtensions", "AddSQuiLLibA");

	[Fact]
	public void With_contexts_emits_marker_public_method_and_internal_aggregator()
	{
		var text = SQuiLDependencyInjectionEmitter.Render("My.App", ["My.App.Ctx"], [LibA]);

		Assert.Contains("[assembly: global::SQuiL.SQuiLRegistration(typeof(global::Microsoft.Extensions.DependencyInjection.SQuiLMyAppExtensions), \"AddSQuiLMyApp\")]", text);
		Assert.Contains("public static class SQuiLMyAppExtensions", text);
		Assert.Contains("services.TryAddSingleton<My.App.Ctx>();", text);
		Assert.Contains("[global::Microsoft.CodeAnalysis.EmbeddedAttribute]", text);
		Assert.Contains("internal static class SQuiLExtensions", text);
		Assert.Contains("global::Microsoft.Extensions.DependencyInjection.SQuiLMyAppExtensions.AddSQuiLMyApp(services);", text);
		Assert.Contains("global::Microsoft.Extensions.DependencyInjection.SQuiLLibAExtensions.AddSQuiLLibA(services);", text);
	}

	[Fact]
	public void Assembly_name_is_escaped_in_doc_comments()
	{
		var text = SQuiLDependencyInjectionEmitter.Render("A&B<C", ["X.Ctx"], []);

		Assert.DoesNotContain("declared in A&B<C", text);
		Assert.Contains("declared in A&amp;B&lt;C", text);
	}

	[Fact]
	public void Without_contexts_emits_only_the_internal_aggregator()
	{
		var text = SQuiLDependencyInjectionEmitter.Render("Host", [], [LibA]);

		Assert.DoesNotContain("[assembly:", text);
		Assert.DoesNotContain("public static class", text);
		Assert.Contains("internal static class SQuiLExtensions", text);
		Assert.Contains("SQuiLLibAExtensions.AddSQuiLLibA(services);", text);
	}
}
