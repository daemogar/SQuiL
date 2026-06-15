namespace SQuiL.Tests.Library;

using SQuiL;

public class SQuiLResultTypeTests
{
	private static SQuiLError Error(int number = 50000)
		=> new(number, 16, 1, 42, "usp_Test", $"Test error {number}");

	[Fact]
	public void SuccessSingletonHasNoErrors()
	{
		Assert.False(SQuiLResultType.Success.HasErrors);
		Assert.False(SQuiLResultType.Success.TryGetErrors(out _));
	}

	[Fact]
	public void SingleErrorWrapsIntoListOfOne()
	{
		SQuiLResultType result = new(Error());

		Assert.True(result.HasErrors);
		Assert.True(result.TryGetErrors(out var errors));
		Assert.Single(errors);
	}

	[Fact]
	public void ErrorListIsExposedVerbatim()
	{
		SQuiLError[] list = [Error(1), Error(2), Error(3)];
		SQuiLResultType result = new(list);

		Assert.True(result.TryGetErrors(out var errors));
		Assert.Equal(list, errors);
	}

	[Fact]
	public void GenericValueResultRoundTrips()
	{
		SQuiLResultType<string> result = new("payload");

		Assert.True(result.IsValue);
		Assert.False(result.HasErrors);
		Assert.True(result.TryGetValue(out var value, out var errors));
		Assert.Equal("payload", value);
		Assert.Null(errors);
	}

	[Fact]
	public void GenericErrorResultReturnsFalseAndErrors()
	{
		SQuiLResultType<string> result = new(Error());

		Assert.False(result.IsValue);
		Assert.True(result.HasErrors);
		Assert.False(result.TryGetValue(out var value, out var errors));
		Assert.Null(value);
		Assert.Single(errors);
	}
}
