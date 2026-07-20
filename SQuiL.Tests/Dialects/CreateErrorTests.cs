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
		var error = new SQuiLError(50000, 16, 1, 7, "usp_Test", "boom")
			.WithException(probe);
		Assert.Same(probe, error.AsDbException());
		// All six positional fields must round-trip — this is the exact mapping
		// SqlServerDataContext.CreateError performs from a SqlException.
		Assert.Equal(50000, error.Number);
		Assert.Equal(16, error.Severity);
		Assert.Equal(1, error.State);
		Assert.Equal(7, error.Line);
		Assert.Equal("usp_Test", error.Procedure);
		Assert.Equal("boom", error.Message);
	}

	sealed class TestDbException(string message) : DbException(message);
}
