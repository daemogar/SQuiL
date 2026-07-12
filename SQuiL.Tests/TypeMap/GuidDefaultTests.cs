namespace SQuiL.Tests.TypeMap;

using Xunit;

public class GuidDefaultTests
{
    private static string Header([System.Runtime.CompilerServices.CallerMemberName] string name = "")
        => TestHelper.TestHeaderPublic(name: name);

    // Regression: a non-null default on uniqueidentifier must emit System.Guid.Parse("..."),
    // not a bare quoted string (would be CS0029). Fixed on master; this locks it.
    [Fact]
    public Task GuidNonNullDefaultCompiles() => TestHelper.Verify(
        [Header()],
        ["""
        --Name: GuidNonNullDefaultCompiles
        Declare @Param_Key uniqueidentifier = '00000000-0000-0000-0000-000000000001';
        Use [Db];
        Select @Param_Key;
        """]);
}
