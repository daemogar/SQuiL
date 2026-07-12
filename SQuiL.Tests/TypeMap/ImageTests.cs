namespace SQuiL.Tests.TypeMap;

using Xunit;

/// <summary>
/// End-to-end proof that <c>image</c> routes to C# <c>byte[]</c> (deprecated T-SQL type,
/// same runtime shape as <c>binary</c>/<c>varbinary</c>), with <c>SqlDbType.Image</c> on the
/// input parameter. No input restriction — declarable as an ordinary <c>@Param_</c> scalar.
/// </summary>
public class ImageTests
{
    private static string Header([System.Runtime.CompilerServices.CallerMemberName] string name = "")
        => TestHelper.TestHeaderPublic(name: name);

    [Fact]
    public Task ImageMapsToByteArray() => TestHelper.Verify(
        [Header()],
        ["""
        --Name: ImageMapsToByteArray
        Declare @Param_Blob image;
        Use [Db];
        Select 1;
        """]);
}
