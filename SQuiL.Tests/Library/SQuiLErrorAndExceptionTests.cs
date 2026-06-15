namespace SQuiL.Tests.Library;

using SQuiL;

public class SQuiLErrorAndExceptionTests
{
	private static SQuiLError Error(int number = 2627)
		=> new(number, 14, 1, 7, "usp_Insert", "Violation of UNIQUE KEY constraint");

	[Fact]
	public void AsExceptionCarriesMessage()
		=> Assert.Equal("Violation of UNIQUE KEY constraint", Error().AsException().Message);

	[Fact]
	public void AsSqlExceptionIsNullWhenRecordConstructed()
		=> Assert.Null(Error().AsSqlException());

	[Fact]
	public void SQuiLExceptionMirrorsErrorFields()
	{
		var ex = Error().AsSQuiLException();

		Assert.Equal(2627, ex.HResult);
		Assert.Equal("Violation of UNIQUE KEY constraint", ex.Message);
		Assert.Equal("SQuiL", ex.Source);
		Assert.Equal("https://github.com/daemogar/SQuiL", ex.HelpLink);
		Assert.Same(ex, ex.GetBaseException());
	}

	[Fact]
	public void SQuiLExceptionDataDictionaryHasAllSixFields()
	{
		var data = Error().AsSQuiLException().Data;

		Assert.Equal(2627, data["Number"]);
		Assert.Equal(14, data["Severity"]);
		Assert.Equal(1, data["State"]);
		Assert.Equal(7, data["Line"]);
		Assert.Equal("usp_Insert", data["Procedure"]);
		Assert.Equal("Violation of UNIQUE KEY constraint", data["Message"]);
	}

	[Fact]
	public void SQuiLExceptionToStringContainsSqlMetadata()
	{
		var text = Error().AsSQuiLException().ToString();

		Assert.Contains("Number: 2627", text);
		Assert.Contains("Severity: 14", text);
		Assert.Contains("Procedure: usp_Insert", text);
		Assert.Contains("Line 7", text);
	}

	[Fact]
	public void SQuiLExceptionEqualityIsByErrorRecord()
	{
		var a = Error().AsSQuiLException();
		var b = Error().AsSQuiLException();

		Assert.True(a.Equals(b));
		Assert.Equal(a.GetHashCode(), b.GetHashCode());
	}

	[Fact]
	public void AggregateExceptionWrapsOneInnerPerError()
	{
		SQuiLError[] errors = [Error(1), Error(2)];
		SQuiLAggregateException aggregate = new(errors);

		Assert.Equal(2, aggregate.InnerExceptions.Count);
		Assert.All(aggregate.InnerExceptions,
			(inner, i) => Assert.Equal(errors[i].Message, inner.Message));
	}
}
