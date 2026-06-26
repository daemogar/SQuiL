namespace SQuiL.Tests.NamespaceOverride;

public class NamespaceOverrideTests : BaseTest
{
	[Fact]
	public Task NamespaceOverrideRelocatesRecords()
	{
		var name = nameof(NamespaceOverrideRelocatesRecords);
		return TestHelper.Verify(
			[TestHeader([name], p => $$"""[SQuiLQuery(QueryFiles.{{p}}, Namespace: "Dto")]""")],
			[$$"""
			--Name: {{name}}
			Declare @Returns_Person table(PersonID int, FullName varchar(100));
			Use [Database];
			Select 1;
			"""]);
	}

	[Fact]
	public Task EmptyNamespaceOverrideEmitsTopLevel()
	{
		var name = nameof(EmptyNamespaceOverrideEmitsTopLevel);
		return TestHelper.Verify(
			[TestHeader([name], p => $$"""[SQuiLQuery(QueryFiles.{{p}}, Namespace: "")]""")],
			[$$"""
			--Name: {{name}}
			Declare @Returns_Person table(PersonID int, FullName varchar(100));
			Use [Database];
			Select 1;
			"""]);
	}

	[Fact]
	public Task TwoContextsWithConflictingNamespace()
	{
		var name = nameof(TwoContextsWithConflictingNamespace);
		return TestHelper.Verify(
			[TestHeader([$"{name}1", $"{name}2"], p =>
			{
				var ns = p.EndsWith("1") ? "A" : "B";
				return $$"""[SQuiLQuery(QueryFiles.{{p}}, Namespace: "{{ns}}")]""";
			})],
			[$$"""
			--Name: {{name}}1
			Declare @Returns_Person table(PersonID int, FullName varchar(100));
			Use [Database];
			Select 1;
			""", $$"""
			--Name: {{name}}2
			Declare @Returns_Person table(PersonID int, FullName varchar(100));
			Use [Database];
			Select 1;
			"""]);
	}
}