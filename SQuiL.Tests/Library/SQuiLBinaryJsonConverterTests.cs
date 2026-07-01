namespace SQuiL.Tests.Library;

using SQuiL;

using System.Text.Json;

public class SQuiLBinaryJsonConverterTests
{
	private static readonly JsonSerializerOptions Options = new()
	{
		Converters = { new SQuiLBinaryJsonConverter() }
	};

	[Fact]
	public void WritesBareUppercaseHexNoPrefix()
	{
		byte[] value = [0x00, 0x0F, 0xAB, 0xFF];
		Assert.Equal("\"000FABFF\"", JsonSerializer.Serialize(value, Options));
	}

	[Fact]
	public void WritesNullAsJsonNull()
	{
		byte[]? value = null;
		Assert.Equal("null", JsonSerializer.Serialize(value, Options));
	}

	[Fact]
	public void RoundTripsThroughHex()
	{
		byte[] value = [1, 2, 3, 250, 255];
		var json = JsonSerializer.Serialize(value, Options);
		var back = JsonSerializer.Deserialize<byte[]>(json, Options);
		Assert.Equal(value, back);
	}

	[Fact]
	public void ReadsNullBack()
	{
		Assert.Null(JsonSerializer.Deserialize<byte[]?>("null", Options));
	}
}
