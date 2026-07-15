using SQuiL.SourceGenerator.Parser;
using SQuiL.Tokenizer;
using System.Linq;
using Xunit;

namespace SQuiL.Tests.NestedObjects;

public class ParserPrimaryKeyTests
{
    private static CodeBlock ParseSingleTable(string sql)
        => SQuiLParser.ParseTokens(SQuiLTokenizer.GetTokens(sql))
            .Single(b => b.IsTable || b.IsObject);

    [Fact]
    public void PrimaryKeyColumnIsFlagged()
    {
        var block = ParseSingleTable(
            "Declare @Returns_Course table(CourseID int Primary Key, Title varchar(20));\nUse Db;\nSelect 1;");
        var pk = block.Properties.Single(p => p.IsPrimaryKey);
        Assert.Equal("CourseID", pk.Identifier.Value);
        Assert.False(block.Properties.Single(p => p.Identifier.Value == "Title").IsPrimaryKey);
    }

    [Fact]
    public void PrimaryKeyCombinesWithNullAndDefaultInAnyOrder()
    {
        // not null before PK
        var a = ParseSingleTable(
            "Declare @Returns_A table(AID int not null Primary Key, N int default 5);\nUse Db;\nSelect 1;");
        Assert.True(a.Properties.Single(p => p.Identifier.Value == "AID").IsPrimaryKey);
        Assert.Equal("5", a.Properties.Single(p => p.Identifier.Value == "N").DefaultValue);
    }
}
