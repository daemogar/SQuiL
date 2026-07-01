namespace SQuiL;

using System;
using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>
/// Serializes <see cref="byte"/>[] columns as a bare (no <c>0x</c> prefix), uppercase
/// hex string so the SQL side can decode them with <c>CONVERT(varbinary(…), [col], 2)</c>.
/// <para>
/// System.Text.Json's default <c>byte[]</c> handling is base64, which a <c>varbinary</c>
/// OPENJSON target does NOT decode (it performs an implicit character→bytes conversion,
/// which is wrong). This converter, paired with <c>CONVERT(…, 2)</c>, gives a correct
/// binary round-trip. <c>null</c> maps to JSON <c>null</c>.
/// </para>
/// Hand-rolled hex (no <c>Convert.ToHexString</c>) because this assembly targets
/// netstandard2.0.
/// </summary>
public sealed class SQuiLBinaryJsonConverter : JsonConverter<byte[]?>
{
	public override byte[]? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
	{
		if (reader.TokenType == JsonTokenType.Null)
			return null;

		var hex = reader.GetString() ?? "";
		var bytes = new byte[hex.Length / 2];
		for (var i = 0; i < bytes.Length; i++)
			bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
		return bytes;
	}

	public override void Write(Utf8JsonWriter writer, byte[]? value, JsonSerializerOptions options)
	{
		if (value is null)
		{
			writer.WriteNullValue();
			return;
		}

		// BitConverter.ToString → "00-0F-AB-FF"; strip dashes for bare hex.
		writer.WriteStringValue(BitConverter.ToString(value).Replace("-", ""));
	}
}
