namespace SQuiL.Tests.TypeMap;

using Xunit;

/// <summary>
/// End-to-end proof that <c>real</c> routes to C# <c>float</c> (not <c>double</c>), including
/// a defaulted scalar emitting an <c>f</c>-suffixed literal (<c>1.5f</c>) rather than a bare
/// <c>double</c> literal, which would fail to compile against a <c>float</c> property.
/// </summary>
public class RealTests
{
    private static string Header([System.Runtime.CompilerServices.CallerMemberName] string name = "")
        => TestHelper.TestHeaderPublic(name: name);

    [Fact]
    public Task RealMapsToFloat() => TestHelper.Verify(
        [Header()],
        ["""
        --Name: RealMapsToFloat
        Declare @Param_Input real;
        Declare @Param_Rate real = 1.5;
        Declare @Return_Output real;
        Use [Db];
        Set @Return_Output = @Param_Input + @Param_Rate;
        """]);
}
