using SQuiL.SourceGenerator.Parser;
namespace SQuiL.Tests;

public class MutationScannerTests
{
    [Theory]
    [InlineData("Select * from [Documents];", true, "Select on real table is read")]
    [InlineData("Insert Into @Rows(Id) Values (1);", true, "Insert into @table-var is read")]
    [InlineData("Update @Rows set Id = 1;", true, "Update @table-var is read")]
    public void ProvablyReadOnly(string body, bool expected, string _)
        => Assert.Equal(expected, SQuiLMutationScanner.Scan(body).IsProvablyReadOnly);

    [Theory]
    [InlineData("Update [Documents] set X = 1;", "Update")]
    [InlineData("Insert Into [Documents](X) Values (1);", "Insert")]
    [InlineData("Delete From dbo.Documents;", "Delete")]
    [InlineData("Merge Into Target using Src on 1=1;", "Merge")]
    public void NotReadOnly_RecordsHit(string body, string kind)
    {
        var r = SQuiLMutationScanner.Scan(body);
        Assert.False(r.IsProvablyReadOnly);
        Assert.Contains(r.Mutations, m => m.Kind == kind);
    }

    [Fact]
    public void DetectsOwnTransaction()
        => Assert.True(SQuiLMutationScanner.Scan("Begin Tran; Update [T] set X=1; Commit;").HasOwnTransaction);

    [Fact]
    public void IgnoresMutationKeywordsInCommentsAndStrings()
        => Assert.True(SQuiLMutationScanner.Scan(
            "-- Update [T]\nSelect 'delete from x' as note;").IsProvablyReadOnly);

    [Fact]
    public void DeleteAtVariableIsReadOnly()
        => Assert.True(SQuiLMutationScanner.Scan("Delete From @TempRows;").IsProvablyReadOnly);

    [Fact]
    public void TruncateIsNotReadOnly()
    {
        var r = SQuiLMutationScanner.Scan("Truncate Table dbo.Logs;");
        Assert.False(r.IsProvablyReadOnly);
        Assert.Contains(r.Mutations, m => m.Kind == "Truncate");
    }

    [Fact]
    public void ExecIsNotReadOnly()
    {
        var r = SQuiLMutationScanner.Scan("Exec sp_DoSomething;");
        Assert.False(r.IsProvablyReadOnly);
        Assert.Contains(r.Mutations, m => m.Kind == "Exec");
    }

    [Fact]
    public void SelectIntoRealTableIsNotReadOnly()
    {
        var r = SQuiLMutationScanner.Scan("Select Id Into dbo.Archive From dbo.Source;");
        Assert.False(r.IsProvablyReadOnly);
        Assert.Contains(r.Mutations, m => m.Kind == "SelectInto");
    }

    [Fact]
    public void SelectIntoAtVariableIsReadOnly()
        => Assert.True(SQuiLMutationScanner.Scan("Select Id Into @Temp From dbo.Source;").IsProvablyReadOnly);

    [Fact]
    public void BeginTransactionKeywordDetected()
        => Assert.True(SQuiLMutationScanner.Scan("Begin Transaction; Select 1; Commit;").HasOwnTransaction);

    [Fact]
    public void NoOwnTransactionWhenAbsent()
        => Assert.False(SQuiLMutationScanner.Scan("Select 1;").HasOwnTransaction);

    [Fact]
    public void MutationKeywordInBlockCommentIgnored()
        => Assert.True(SQuiLMutationScanner.Scan("/* Insert Into RealTable values(1) */ Select 1;").IsProvablyReadOnly);
}
