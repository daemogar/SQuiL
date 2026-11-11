namespace SQuiL.Tests.ExtensionParsing;

using SQuiL.SsmsExtension.Parsing;
using SQuiL.SsmsExtension.Preview;
using Xunit;

/// <summary>
/// The SSMS / Visual Studio C# preview mirrors the generator's <c>required</c> request members
/// (see <see cref="SQuiL.Tests.RequiredRequestMemberTests"/>): a non-nullable reference-type
/// request scalar with no default previews as <c>required</c>; nullable, defaulted, value-type
/// and response scalars do not. The Visual Studio copy is byte-identical modulo namespace.
/// </summary>
public class PreviewRequiredMemberTests
{
	private static string Preview() => SQuiLPreviewGenerator.Generate(SQuiLParser.Parse(string.Join("\n",
		"--Name: Save",
		"Declare @Param_WorkID varchar(36);",
		"Declare @Param_Note varchar(50) = null;",
		"Declare @Param_Label varchar(10) = 'x';",
		"Declare @Param_Payload varbinary(max);",
		"Declare @Param_Count int;",
		"Declare @Return_Name varchar(10);",
		"Use Db;",
		"Select @Return_Name = Name From Work Where WorkID = @Param_WorkID;",
		"Select @Return_Name;")), "Save");

	[Fact]
	public void RequiredReferenceTypeParamsPreviewAsRequired()
	{
		var output = Preview();

		Assert.Contains("public required string WorkID { get; set; }", output);
		Assert.Contains("public required byte[] Payload { get; set; }", output);
		Assert.DoesNotMatch(@"required \S+ Note\b", output);
		Assert.DoesNotContain("required string Label", output);
		Assert.DoesNotContain("required int", output);
		Assert.DoesNotContain("required string Name", output);
	}
}
