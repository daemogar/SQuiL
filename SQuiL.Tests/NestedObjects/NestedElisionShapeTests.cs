namespace SQuiL.Tests.NestedObjects;

using System.Linq;

using Xunit;

/// <summary>
/// SP0017 must fire when declarations sharing one record disagree on which key columns an
/// embed elides (R4) — otherwise the first registrant silently decides the record shape.
/// </summary>
public class NestedElisionShapeTests
{
	private const string EmbeddingQuery = """
		--Name: Embedding
		Declare @Returns_Structure table(Title varchar(50) not null, ContactID varchar(10) not null);
		Declare @Returns_Contact table(ContactID varchar(10) not null Primary Key, Name varchar(50) not null);
		Use [Db];
		Select * From @Returns_Structure;
		Select * From @Returns_Contact;
		""";

	private const string EmbeddingQueryTwo = """
		--Name: EmbeddingTwo
		Declare @Returns_Structure table(Title varchar(50) not null, ContactID varchar(10) not null);
		Declare @Returns_Contact table(ContactID varchar(10) not null Primary Key, Name varchar(50) not null);
		Use [Db];
		Select * From @Returns_Structure;
		Select * From @Returns_Contact;
		""";

	private const string FlatQuery = """
		--Name: Flat
		Declare @Returns_Structure table(Title varchar(50) not null, ContactID varchar(10) not null);
		Use [Db];
		Select * From @Returns_Structure;
		""";

	private static Microsoft.CodeAnalysis.GeneratorDriverRunResult Run(string[] queryNames, string[] queries)
		=> TestHelper.RunForDiagnosticsAndSources(
			[TestHelper.TestHeaderPublic(queryNames, name: "Shape")],
			queries,
			includeSqlServer: true,
			includeSqlite: false);

	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public void CrossFileEmbedVersusFlatReportsSP0017(bool embeddingFirst)
	{
		var result = embeddingFirst
			? Run(["Embedding", "Flat"], [EmbeddingQuery, FlatQuery])
			: Run(["Flat", "Embedding"], [FlatQuery, EmbeddingQuery]);

		var sp0017 = result.Diagnostics.Where(d => d.Id == "SP0017").ToList();
		Assert.NotEmpty(sp0017);
		Assert.Contains(sp0017, d => d.GetMessage().Contains("Structure")
			&& d.GetMessage().Contains("ContactID varchar(10) [embedded key]")
			&& d.GetMessage().Contains("embedded lookup removes its key column"));
	}

	[Fact]
	public void SameFileFlatInputVersusEmbeddingOutputReportsSP0017()
	{
		var result = Run(["CrossSide"], ["""
			--Name: CrossSide
			Declare @Params_Structure table(Title varchar(50) not null, ContactID varchar(10) not null);
			Declare @Returns_Structure table(Title varchar(50) not null, ContactID varchar(10) not null);
			Declare @Returns_Contact table(ContactID varchar(10) not null Primary Key, Name varchar(50) not null);
			Use [Db];
			Select * From @Returns_Structure;
			Select * From @Returns_Contact;
			"""]);

		Assert.Contains(result.Diagnostics, d => d.Id == "SP0017");
	}

	[Fact]
	public void TwoFilesEmbeddingIdenticallyShareTheRecordWithoutSP0017()
	{
		var result = Run(["Embedding", "EmbeddingTwo"], [EmbeddingQuery, EmbeddingQueryTwo]);

		Assert.DoesNotContain(result.Diagnostics, d => d.Id == "SP0017");
		Assert.Contains(result.GeneratedTrees, t => t.FilePath.EndsWith("Structure.g.cs"));
	}
}
