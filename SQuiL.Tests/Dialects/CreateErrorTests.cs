namespace SQuiL.Tests.Dialects;

using global::SQuiL;

using System.Data.Common;

using Xunit;

public class CreateErrorTests
{
	[Fact]
	public void SQuiLError_exposes_underlying_DbException_via_AsDbException()
	{
		DbException probe = new TestDbException("boom");
		var error = new SQuiLError(50000, 16, 1, 7, "usp_Test", "boom") { }
			.WithException(probe);   // see Step 3 for the internal setter used by CreateError
		Assert.Same(probe, error.AsDbException());
		Assert.Equal("boom", error.Message);
		Assert.Equal(16, error.Severity);
	}

	sealed class TestDbException(string message) : DbException(message);
}
