namespace SQuiL;

using System.Data.Common;
using System.Text;

public abstract partial class SQuiLBaseDataContext
{
	/// <summary>
	/// Computes the ordered signature key of the reader's current result set:
	/// columns joined by '|', each "name:canonicalType", names lower-cased.
	/// MUST match the build-time key from SQuiLShapeKey.ShapeKeyOf. Dispatches to the
	/// provider subclass's <see cref="NormalizeType"/> override, so the emitted
	/// (unqualified) <c>ShapeKey(reader)</c> call resolves to the correct provider's
	/// type map without the generator needing to pass a dialect argument.
	/// </summary>
	protected string ShapeKey(DbDataReader reader)
	{
		var sb = new StringBuilder();
		for (var i = 0; i < reader.FieldCount; i++)
		{
			if (i > 0) sb.Append('|');
			sb.Append(reader.GetName(i).ToLowerInvariant());
			sb.Append(':');
			sb.Append(NormalizeType(reader.GetDataTypeName(i)));
		}
		return sb.ToString();
	}

	/// <summary>
	/// Provider type name -> canonical C# type token (matching Token.CSharpType).
	/// Length/precision ignored. Unknown types pass through lower-cased so they simply
	/// fail to match any declared output (clean skip). Neutral base passthrough; every
	/// provider (<c>SqlServerDataContext</c>, <c>SqliteDataContext</c>, ...) overrides this
	/// with its own map. This is the dialect seam for TODO #6 / Phase 3B.
	/// </summary>
	protected virtual string NormalizeType(string providerTypeName) => providerTypeName.ToLowerInvariant();

	internal string NormalizeTypeForTest(string providerTypeName) => NormalizeType(providerTypeName);
}
