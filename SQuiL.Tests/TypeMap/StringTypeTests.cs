namespace SQuiL.Tests.TypeMap;

using Xunit;

/// <summary>
/// End-to-end proof that each string spelling binds its scalar parameter as the declared
/// <c>SqlDbType</c> (TODO #31), and that sizeless <c>text</c>/<c>ntext</c> compile.
/// </summary>
public class StringTypeTests
{
    private static string Header([System.Runtime.CompilerServices.CallerMemberName] string name = "")
        => TestHelper.TestHeaderPublic(name: name);

    [Fact]
    public Task StringTypesBindAsDeclared() => TestHelper.Verify(
        [Header()],
        ["""
        --Name: StringTypesBindAsDeclared
        Declare @Param_Code char(5);
        Declare @Param_UCode nchar(5);
        Declare @Param_Name varchar(10);
        Declare @Param_UName nvarchar(10);
        Declare @Param_Body nvarchar(max);
        Declare @Param_Old text;
        Declare @Param_UOld ntext = null;
        Use [Db];
        Select @Param_Code, @Param_UCode, @Param_Name, @Param_UName, @Param_Body, @Param_Old, @Param_UOld;
        """]);
}
