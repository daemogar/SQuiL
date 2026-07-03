namespace SQuiL.Tests.ShapeDetection;

using SQuiL.SourceGenerator.Parser;
using SQuiL.Models;
using SQuiL.Tokenizer;
using Xunit;

public class ShapeKeyTests
{
    private static CodeBlock ParseSingleBlock(string declare)
    {
        var tokens = SQuiLTokenizer.GetTokens($"{declare}\nUse [Db];\nSelect 1;");
        var blocks = SQuiLParser.ParseTokens(tokens);
        return blocks.Find(b => b.IsTable || b.IsObject)!;
    }

    [Fact]
    public void TableKey_IsOrderedNameColonType_Lowercased()
    {
        var block = ParseSingleBlock("Declare @Returns_People table(PersonID int, Name varchar(100));");
        Assert.Equal("personid:int|name:string", SQuiLShapeKey.ShapeKeyOf(block));
    }

    [Fact]
    public void Key_IgnoresLength_And_Nullability()
    {
        var a = ParseSingleBlock("Declare @Returns_A table(Note varchar(50) null);");
        var b = ParseSingleBlock("Declare @Returns_B table(Note varchar(4000));");
        Assert.Equal(SQuiLShapeKey.ShapeKeyOf(a), SQuiLShapeKey.ShapeKeyOf(b));
    }

    [Fact]
    public void Key_DistinguishesOrder()
    {
        var a = ParseSingleBlock("Declare @Returns_A table(PersonID int, Name varchar(100));");
        var b = ParseSingleBlock("Declare @Returns_B table(Name varchar(100), PersonID int);");
        Assert.NotEqual(SQuiLShapeKey.ShapeKeyOf(a), SQuiLShapeKey.ShapeKeyOf(b));
    }
}
