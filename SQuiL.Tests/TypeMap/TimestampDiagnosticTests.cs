namespace SQuiL.Tests.TypeMap;

using Xunit;

public class TimestampDiagnosticTests
{
    private static string Header([System.Runtime.CompilerServices.CallerMemberName] string name = "")
        => TestHelper.TestHeaderPublic(name: name);

    // SP0032: timestamp as an input @Param is a build error (compileCheck off — error path).
    [Fact]
    public Task TimestampInputParamIsError() => TestHelper.Verify(
        [Header()],
        ["""
        --Name: TimestampInputParamIsError
        Declare @Param_Version timestamp;
        Use [Db];
        Select 1;
        """], compileCheck: false);

    // timestamp on an output @Return routes fine (byte[]) — no SP0032.
    [Fact]
    public Task TimestampOutputIsAllowed() => TestHelper.Verify(
        [Header()],
        ["""
        --Name: TimestampOutputIsAllowed
        Declare @Return_Version timestamp;
        Use [Db];
        Select @Return_Version;
        """]);
}
