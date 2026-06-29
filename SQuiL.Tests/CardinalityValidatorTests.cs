using SQuiL.SourceGenerator.Parser;
using SQuiL.Tokenizer;

namespace SQuiL.Tests;

/// <summary>
/// Unit tests for <see cref="SQuiLCardinalityValidator"/> — SP0022 fires when one
/// base name is declared as both a table (list) and a single object on the SAME side
/// within one file. Cross-side and same-cardinality declarations are silent; cross-file
/// is impossible here (the detector sees one file's blocks).
/// </summary>
public class CardinalityValidatorTests
{
    private static List<SQuiLCardinalityValidator.Finding> Detect(string sql)
        => SQuiLCardinalityValidator.Detect(
            SQuiLParser.ParseTokens(SQuiLTokenizer.GetTokens(sql)), sql);

    [Fact]
    public void Output_list_and_object_same_name_collides()
    {
        var findings = Detect("""
            Declare @Returns_Person table(PersonID int, FullName varchar(100));
            Declare @Return_Person table(PersonID int, FullName varchar(100));
            Use MyDatabase;
            Select 1;
            """);

        var f = Assert.Single(findings);
        Assert.Equal("Person", f.Name);
        Assert.True(f.IsOutput);
        Assert.False(f.DroppedIsTable);   // @Return_Person (single object) is dropped
        Assert.True(f.FirstIsTable);      // @Returns_Person (list) was declared first and wins
        Assert.Equal(2, f.DroppedLine);
        Assert.Equal(1, f.FirstLine);
    }

    [Fact]
    public void Input_list_and_object_same_name_collides()
    {
        var findings = Detect("""
            Declare @Params_Rows table(RowID int);
            Declare @Param_Rows table(RowID int);
            Use MyDatabase;
            Select 1;
            """);

        var f = Assert.Single(findings);
        Assert.Equal("Rows", f.Name);
        Assert.False(f.IsOutput);
    }

    [Fact]
    public void Same_cardinality_same_name_does_not_collide()
    {
        // two lists, same name — legitimate dedup, NOT a cardinality collision.
        var findings = Detect("""
            Declare @Returns_Person table(PersonID int);
            Declare @Returns_Person table(PersonID int);
            Use MyDatabase;
            Select 1;
            """);

        Assert.Empty(findings);
    }

    [Fact]
    public void Cross_side_same_name_does_not_collide()
    {
        // input list + output object, same name — different models, no collision.
        var findings = Detect("""
            Declare @Params_Person table(PersonID int);
            Declare @Return_Person table(PersonID int);
            Use MyDatabase;
            Select 1;
            """);

        Assert.Empty(findings);
    }

    [Fact]
    public void Plain_query_has_no_collision()
    {
        var findings = Detect("""
            Declare @Param_Name varchar(100);
            Declare @Returns_People table(PersonID int);
            Use MyDatabase;
            Select 1;
            """);

        Assert.Empty(findings);
    }

    [Fact]
    public void Three_same_name_decls_flag_only_cardinality_mismatch()
    {
        // @Returns_X (list) + @Return_X (object) + @Returns_X (list): only the object
        // (cardinality mismatch vs the first-declared list) is flagged; the duplicate
        // list is a same-cardinality dedup, NOT a cardinality collision.
        var findings = Detect("""
            Declare @Returns_X table(ID int);
            Declare @Return_X table(ID int);
            Declare @Returns_X table(ID int);
            Use MyDatabase;
            Select 1;
            """);

        var f = Assert.Single(findings);
        Assert.Equal("X", f.Name);
        Assert.False(f.DroppedIsTable);   // the @Return_X object is the one flagged
        Assert.Equal(2, f.DroppedLine);
        Assert.Equal(1, f.FirstLine);
    }
}
