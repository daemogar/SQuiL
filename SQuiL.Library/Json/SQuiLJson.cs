namespace SQuiL;

using System.Text.Json;

/// <summary>
/// Centralized, pinned JSON serialization for SQuiL param-sharding.
/// The options are tuned so the produced JSON is consumed correctly by
/// <c>OPENJSON … WITH (&lt;sqltype&gt;)</c>:
/// <list type="bullet">
///   <item>Property names are emitted verbatim (no camelCase policy) so they
///   match the <c>$.&lt;Column&gt;</c> paths the generator emits.</item>
///   <item>Numbers, <c>bit</c>→<c>true</c>/<c>false</c>, ISO-8601 dates, and GUID
///   strings are System.Text.Json defaults that OPENJSON reads natively.</item>
///   <item><c>byte[]</c> is written as bare hex via
///   <see cref="SQuiLBinaryJsonConverter"/> (decoded SQL-side with <c>CONVERT(…, 2)</c>).</item>
/// </list>
/// </summary>
public static class SQuiLJson
{
	private static readonly JsonSerializerOptions Options = new()
	{
		Converters = { new SQuiLBinaryJsonConverter() }
	};

	/// <summary>Serializes <paramref name="value"/> with the pinned SQuiL options.</summary>
	public static string Serialize(object? value) => JsonSerializer.Serialize(value, Options);
}
