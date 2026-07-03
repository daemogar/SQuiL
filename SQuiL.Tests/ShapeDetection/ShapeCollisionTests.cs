namespace SQuiL.Tests.ShapeDetection;

using System.Threading.Tasks;
using Xunit;

public class ShapeCollisionTests
{
    [Fact]
    public Task IdenticalOutputSignaturesCollideSP0030()
    {
        var name = nameof(IdenticalOutputSignaturesCollideSP0030);
        return TestHelper.Verify([TestHelper.TestHeaderPublic([name])], [$$"""
            --Name: {{name}}
            Declare @Returns_Active table(PersonID int, Name varchar(100));
            Declare @Returns_Inactive table(PersonID int, Name varchar(100));
            Use [Db];
            Select * From @Returns_Active;
            Select * From @Returns_Inactive;
            """], compileCheck: false);
    }

    [Fact]
    public Task LengthOnlyDifferenceStillCollidesSP0030()
    {
        var name = nameof(LengthOnlyDifferenceStillCollidesSP0030);
        return TestHelper.Verify([TestHelper.TestHeaderPublic([name])], [$$"""
            --Name: {{name}}
            Declare @Returns_A table(Note varchar(50));
            Declare @Returns_B table(Note varchar(100));
            Use [Db];
            Select * From @Returns_A;
            Select * From @Returns_B;
            """], compileCheck: false);
    }
}
