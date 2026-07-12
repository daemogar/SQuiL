namespace SQuiL.Tests.TypeMap;

using Xunit;

public class VarcharMaxColumnTests
{
    private static string Header([System.Runtime.CompilerServices.CallerMemberName] string name = "")
        => TestHelper.TestHeaderPublic(name: name);

    [Fact]
    public Task VarcharMaxColumnEmitsNoLengthGuard() => TestHelper.Verify(
        [Header()],
        ["""
        --Name: VarcharMaxColumnEmitsNoLengthGuard
        Declare @Params_Rows table(RowID int, Note varchar(max));
        Use [Db];
        Select * From @Params_Rows;
        """]);
}
