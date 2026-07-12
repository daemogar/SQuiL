using SQuiL.Tokenizer;
using Xunit;

namespace SQuiL.Tests.NestedObjects;

public class TokenizerPrimaryKeyTests
{
    [Fact]
    public void PrimaryKeyTokenizesAsSingleConstraintToken()
    {
        var tokens = SQuiLTokenizer.GetTokens(
            "Declare @Returns_Course table(CourseID int Primary Key, Title varchar(20));\nUse Db;\nSelect 1;");

        Assert.Contains(tokens, t => t.Type == TokenType.TYPE_PRIMARY_KEY);
        // "Primary" / "Key" must NOT leak through as identifiers.
        Assert.DoesNotContain(tokens, t => t.Type == TokenType.IDENTIFIER && t.Value == "Primary");
        Assert.DoesNotContain(tokens, t => t.Type == TokenType.IDENTIFIER && t.Value == "Key");
    }

    [Fact]
    public void PrimaryKeyIsCaseInsensitiveAndTolerantOfExtraSpaces()
    {
        var tokens = SQuiLTokenizer.GetTokens(
            "Declare @Returns_X table(XID int   primary   KEY);\nUse Db;\nSelect 1;");
        Assert.Contains(tokens, t => t.Type == TokenType.TYPE_PRIMARY_KEY);
    }
}
