using SQuiL.Models;
using SQuiL.SourceGenerator.Parser;

namespace SQuiL.Tests.ParamSharding;

public class ShredSqlTests
{
	private static CodeBlock TableBlock()
	{
		// Build a @Params_Table table block with TableID int, IsFemale bit, LastName varchar(100).
		// Uses the real tokenizer/parser so column metadata is realistic.
		var sql = """
			Declare @Params_Table table(TableID int, IsFemale bit, LastName varchar(100));
			Use [Database];
			Select 1;
			""";
		return ParserTestSupport.FirstInputBlock(sql);
	}

	[Fact]
	public void TableShredEmitsOpenJsonInsertSelect()
	{
		var sql = SQuiLShred.ShredSql(TableBlock());

		Assert.Contains("Insert Into @Params_Table([TableID], [IsFemale], [LastName])", sql);
		Assert.Contains("From OpenJson(@__json_Params_Table)", sql);
		Assert.Contains("[TableID] int '$.TableID'", sql);
		Assert.Contains("[IsFemale] bit '$.IsFemale'", sql);
		Assert.Contains("[LastName] varchar(100) '$.LastName'", sql);
		Assert.DoesNotContain("Values", sql);          // no VALUES list
		Assert.DoesNotContain("__SQuiL__Table__Type__", sql);  // sentinel excluded
	}

	[Fact]
	public void JsonParamNameUsesPluralForTable()
		=> Assert.Equal("@__json_Params_Table", SQuiLShred.JsonParamName(TableBlock()));
}
