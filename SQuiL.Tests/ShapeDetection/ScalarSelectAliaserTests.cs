namespace SQuiL.Tests.ShapeDetection;

using SQuiL.Dialects;

using System.Collections.Generic;

using Xunit;

/// <summary>
/// Unit tests for the pure scalar-select alias scanner. The scanner is the single source of the
/// SQL-text rules behind the SQL Server implicit-alias rewrite (Task 2), SP0041 (Task 4), and
/// SP0042 (Tasks 5-6), so its edge cases are pinned here rather than through snapshots.
/// </summary>
public class ScalarSelectAliaserTests
{
    /// <summary>One declared output scalar, `@Return_Count` → base name `Count`.</summary>
    private static Dictionary<string, string> Count()
        => new() { ["@return_count"] = "Count" };

    private static Dictionary<string, string> CountAndTotal()
        => new() { ["@return_count"] = "Count", ["@return_total"] = "Total" };

    [Fact]
    public void Rewrite_bare_select_with_semicolon()
        => Assert.Equal(
            "Select @Return_Count As Count;",
            ScalarSelectAliaser.Rewrite("Select @Return_Count;", Count()));

    [Fact]
    public void Rewrite_bare_select_at_end_of_text_without_semicolon()
        => Assert.Equal(
            "Select @Return_Count As Count",
            ScalarSelectAliaser.Rewrite("Select @Return_Count", Count()));

    [Fact]
    public void Rewrite_bare_select_followed_by_another_statement_without_semicolon()
        => Assert.Equal(
            "Select @Return_Count As Count\nUpdate T Set X = 1",
            ScalarSelectAliaser.Rewrite("Select @Return_Count\nUpdate T Set X = 1", Count()));

    [Fact]
    public void Rewrite_is_case_insensitive_and_emits_declared_casing()
        => Assert.Equal(
            "select @return_count As Count;",
            ScalarSelectAliaser.Rewrite("select @return_count;", Count()));

    [Fact]
    public void Rewrite_leaves_an_already_aliased_select_alone()
        => Assert.Equal(
            "Select @Return_Count As Count;",
            ScalarSelectAliaser.Rewrite("Select @Return_Count As Count;", Count()));

    [Fact]
    public void Rewrite_leaves_a_mismatched_alias_alone()
        => Assert.Equal(
            "Select @Return_Count As Foo;",
            ScalarSelectAliaser.Rewrite("Select @Return_Count As Foo;", Count()));

    [Fact]
    public void Rewrite_leaves_the_assignment_form_alone()
        => Assert.Equal(
            "Select @Return_Count = Count(*) From People;",
            ScalarSelectAliaser.Rewrite("Select @Return_Count = Count(*) From People;", Count()));

    [Fact]
    public void Rewrite_leaves_a_multi_scalar_select_alone()
        => Assert.Equal(
            "Select @Return_Count, @Return_Total;",
            ScalarSelectAliaser.Rewrite("Select @Return_Count, @Return_Total;", CountAndTotal()));

    [Fact]
    public void Rewrite_leaves_an_expression_select_alone()
        => Assert.Equal(
            "Select @Return_Count + 1;",
            ScalarSelectAliaser.Rewrite("Select @Return_Count + 1;", Count()));

    [Fact]
    public void Rewrite_leaves_a_select_with_from_alone()
        => Assert.Equal(
            "Select @Return_Count From T;",
            ScalarSelectAliaser.Rewrite("Select @Return_Count From T;", Count()));

    [Fact]
    public void Rewrite_ignores_an_undeclared_variable()
        => Assert.Equal(
            "Select @Return_Other;",
            ScalarSelectAliaser.Rewrite("Select @Return_Other;", Count()));

    [Fact]
    public void Rewrite_ignores_a_line_comment()
        => Assert.Equal(
            "-- Select @Return_Count;\nSelect 1;",
            ScalarSelectAliaser.Rewrite("-- Select @Return_Count;\nSelect 1;", Count()));

    [Fact]
    public void Rewrite_ignores_a_block_comment()
        => Assert.Equal(
            "/* Select @Return_Count; */\nSelect 1;",
            ScalarSelectAliaser.Rewrite("/* Select @Return_Count; */\nSelect 1;", Count()));

    /// <summary>T-SQL <c>/* */</c> comments nest; the whole span is one comment, so the select
    /// inside it must not be aliased.</summary>
    [Fact]
    public void Rewrite_ignores_a_select_inside_a_nested_block_comment()
        => Assert.Equal(
            "/* /* nested */ Select @Return_Count; */",
            ScalarSelectAliaser.Rewrite("/* /* nested */ Select @Return_Count; */", Count()));

    /// <summary>A quote appearing after the nested comment's TRUE close (not its first, inner
    /// <c>*/</c>) must not be mistaken for a string literal that swallows the rest of the text —
    /// the live select following the comment must still be aliased.</summary>
    [Fact]
    public void Rewrite_aliases_a_live_select_following_a_nested_block_comment_containing_an_apostrophe()
        => Assert.Equal(
            "/* /* nested */ it's dead */ Select @Return_Count As Count;",
            ScalarSelectAliaser.Rewrite("/* /* nested */ it's dead */ Select @Return_Count;", Count()));

    [Fact]
    public void Rewrite_ignores_a_string_literal()
        => Assert.Equal(
            "Select 'Select @Return_Count;' As Note;",
            ScalarSelectAliaser.Rewrite("Select 'Select @Return_Count;' As Note;", Count()));

    [Fact]
    public void Rewrite_skips_a_comment_between_the_variable_and_the_semicolon()
        => Assert.Equal(
            "Select @Return_Count As Count /* trailing */;",
            ScalarSelectAliaser.Rewrite("Select @Return_Count /* trailing */;", Count()));

    [Fact]
    public void Rewrite_handles_two_bare_selects()
        => Assert.Equal(
            "Select @Return_Count As Count;\nSelect @Return_Total As Total;",
            ScalarSelectAliaser.Rewrite("Select @Return_Count;\nSelect @Return_Total;", CountAndTotal()));

    [Fact]
    public void FindBareSelects_reports_the_declared_name_and_insert_offset()
    {
        const string text = "Select @Return_Count;";
        var found = ScalarSelectAliaser.FindBareSelects(text, Count());
        Assert.Single(found);
        Assert.Equal("Count", found[0].DeclaredName);
        Assert.Equal(text.IndexOf("@Return_Count"), found[0].VariableOffset);
        Assert.Equal(text.IndexOf(";"), found[0].InsertOffset);
    }

    [Fact]
    public void FindMultiScalarSelects_reports_both_names()
    {
        var found = ScalarSelectAliaser.FindMultiScalarSelects(
            "Select @Return_Count, @Return_Total;", CountAndTotal());
        Assert.Single(found);
        Assert.Equal(new[] { "Count", "Total" }, found[0].DeclaredNames);
    }

    [Fact]
    public void FindMultiScalarSelects_reports_an_aliased_multi_scalar_select()
    {
        var found = ScalarSelectAliaser.FindMultiScalarSelects(
            "Select @Return_Count As Count, @Return_Total As Total;", CountAndTotal());
        Assert.Single(found);
        Assert.Equal(new[] { "Count", "Total" }, found[0].DeclaredNames);
    }

    [Fact]
    public void FindMultiScalarSelects_ignores_a_single_scalar_select()
        => Assert.Empty(ScalarSelectAliaser.FindMultiScalarSelects("Select @Return_Count;", Count()));

    [Fact]
    public void FindMultiScalarSelects_ignores_a_mixed_column_list()
        => Assert.Empty(ScalarSelectAliaser.FindMultiScalarSelects(
            "Select @Return_Count, SomeColumn From T;", Count()));
}
