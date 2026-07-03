namespace SQuiL;

using System.Data.Common;
using System.Text;

public abstract partial class SQuiLBaseDataContext
{
	/// <summary>
	/// Computes the ordered signature key of the reader's current result set:
	/// columns joined by '|', each "name:canonicalType", names lower-cased.
	/// MUST match the build-time key from SQuiLShapeKey.ShapeKeyOf.
	/// </summary>
	protected static string ShapeKey(DbDataReader reader)
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
	/// SQL Server dialect: provider type name -> canonical C# type token (matching
	/// Token.CSharpType). Length/precision ignored. Unknown types pass through
	/// lower-cased so they simply fail to match any declared output (clean skip).
	/// This is the dialect seam for TODO #6 (other providers add their own map).
	/// </summary>
	internal static string NormalizeType(string providerTypeName) => providerTypeName.ToLowerInvariant() switch
	{
		"bit" => "bool",
		"int" => "int",
		"decimal" or "numeric" or "money" or "smallmoney" => "decimal",
		"varchar" or "nvarchar" or "char" or "nchar" or "text" or "ntext" => "string",
		"date" => "System.DateOnly",
		"time" => "System.TimeOnly",
		"datetime" or "datetime2" or "smalldatetime" => "System.DateTime",
		"datetimeoffset" => "System.DateTimeOffset",
		"uniqueidentifier" => "System.Guid",
		"binary" or "varbinary" or "image" or "timestamp" or "rowversion" => "byte[]",
		"float" or "real" => "double",
		var other => other,
	};

	internal static string NormalizeTypeForTest(string providerTypeName) => NormalizeType(providerTypeName);
}
