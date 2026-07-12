namespace SQuiL.Tests.TypeMap;

using Xunit;

/// <summary>
/// End-to-end proof that <c>smalldatetime</c> routes to C# <c>System.DateTime</c> and
/// <c>xml</c> routes to C# <c>string</c> request properties (with distinct
/// <c>SqlDbType.SmallDateTime</c>/<c>SqlDbType.Xml</c> parameter binding, exercised
/// separately by <see cref="TypeMapTests"/>).
/// </summary>
public class XmlSmallDateTimeTests
{
    private static string Header([System.Runtime.CompilerServices.CallerMemberName] string name = "")
        => TestHelper.TestHeaderPublic(name: name);

    [Fact]
    public Task XmlAndSmallDateTimeMapToStringAndDateTime() => TestHelper.Verify(
        [Header()],
        ["""
        --Name: XmlAndSmallDateTimeMapToStringAndDateTime
        Declare @Param_Doc xml;
        Declare @Param_When smalldatetime;
        Use [Db];
        Select @Param_Doc, @Param_When;
        """]);
}
