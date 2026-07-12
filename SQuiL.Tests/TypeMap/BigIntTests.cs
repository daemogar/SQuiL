namespace SQuiL.Tests.TypeMap;

using Xunit;

/// <summary>
/// End-to-end proof that <c>bigint</c>/<c>smallint</c>/<c>tinyint</c> route to C#
/// <c>long</c>/<c>short</c>/<c>byte</c> request properties, respectively.
/// </summary>
public class BigIntTests
{
    private static string Header([System.Runtime.CompilerServices.CallerMemberName] string name = "")
        => TestHelper.TestHeaderPublic(name: name);

    [Fact]
    public Task IntegerTypesMapToLongShortByte() => TestHelper.Verify(
        [Header()],
        ["""
        --Name: IntegerTypesMapToLongShortByte
        Declare @Param_A bigint;
        Declare @Param_B smallint;
        Declare @Param_C tinyint;
        Use [Db];
        Select @Param_A, @Param_B, @Param_C;
        """]);
}
