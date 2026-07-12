namespace SQuiL.Tests.TypeMap;

using Xunit;

/// <summary>
/// End-to-end proof that <c>money</c>/<c>smallmoney</c> route to C# <c>decimal</c>
/// request properties (with distinct <c>SqlDbType.Money</c>/<c>SqlDbType.SmallMoney</c>
/// parameter binding, exercised separately by <see cref="TypeMapTests"/>).
/// </summary>
public class MoneyTests
{
    private static string Header([System.Runtime.CompilerServices.CallerMemberName] string name = "")
        => TestHelper.TestHeaderPublic(name: name);

    [Fact]
    public Task MoneyTypesMapToDecimal() => TestHelper.Verify(
        [Header()],
        ["""
        --Name: MoneyTypesMapToDecimal
        Declare @Param_Price money;
        Declare @Param_Fee smallmoney;
        Use [Db];
        Select @Param_Price, @Param_Fee;
        """]);
}
