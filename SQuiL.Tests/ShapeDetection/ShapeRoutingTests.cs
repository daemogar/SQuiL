namespace SQuiL.Tests.ShapeDetection;

using System.Threading.Tasks;
using Xunit;

public class ShapeRoutingTests
{
    [Fact]
    public Task TwoDistinctOutputsRouteByShape()
    {
        var name = nameof(TwoDistinctOutputsRouteByShape);
        return TestHelper.Verify([TestHelper.TestHeaderPublic([name])], [$$"""
            --Name: {{name}}
            Declare @Returns_People table(PersonID int, Name varchar(100));
            Declare @Returns_Orders table(OrderID int, Total decimal(18,2));
            Use [Db];
            Insert Into @Returns_People Select PersonID, Name From People;
            Insert Into @Returns_Orders Select OrderID, Total From Orders;
            Select * From @Returns_People;
            Select * From @Returns_Orders;
            """]);
    }

    [Fact]
    public Task ScalarReturnRoutesBySingleColumnShape()
    {
        var name = nameof(ScalarReturnRoutesBySingleColumnShape);
        return TestHelper.Verify([TestHelper.TestHeaderPublic([name])], [$$"""
            --Name: {{name}}
            Declare @Return_Count int;
            Use [Db];
            Set @Return_Count = (Select Count(*) From People);
            Select @Return_Count As Count;
            """]);
    }
}
