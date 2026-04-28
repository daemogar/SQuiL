namespace SQuiL.SourceGenerator.Parser;

/// <summary>
/// Flags enumeration that classifies each <see cref="CodeBlock"/> parsed from a SQL file.
/// Bits are composed to express both direction (input vs. output) and shape (scalar, object, table).
/// </summary>
[Flags]
public enum CodeType
{
	/// <summary>A <c>USE [Database];</c> statement that names the target database.</summary>
	USING = 1,
	/// <summary>The SQL query body that follows the USE statement.</summary>
	BODY = 1 << 2,
	/// <summary>Base flag for any input parameter direction.</summary>
	INPUT = 1 << 3,
	/// <summary>A scalar input parameter: <c>@Param_Name type</c>.</summary>
	INPUT_ARGUMENT = INPUT | ARGUMENT,
	/// <summary>A single-row structured input parameter: <c>@Param_Name table(...)</c>.</summary>
	INPUT_OBJECT = INPUT | OBJECT,
	/// <summary>A multi-row table-valued input parameter: <c>@Params_Name table(...)</c>.</summary>
	INPUT_TABLE = INPUT | TABLE,
	/// <summary>Base flag for any output return direction.</summary>
	OUTPUT = 1 << 4,
	/// <summary>A scalar output variable: <c>@Return_Name type</c>.</summary>
	OUTPUT_VARIABLE = OUTPUT | VARIABLE,
	/// <summary>A single-row structured output: <c>@Return_Name table(...)</c>.</summary>
	OUTPUT_OBJECT = OUTPUT | OBJECT,
	/// <summary>A multi-row table output: <c>@Returns_Name table(...)</c>.</summary>
	OUTPUT_TABLE = OUTPUT | TABLE,
	/// <summary>Shape flag indicating a scalar argument (aliased as <see cref="VARIABLE"/>).</summary>
	ARGUMENT = 1 << 5,
	/// <summary>Shape flag indicating a scalar variable (same bit as <see cref="ARGUMENT"/>).</summary>
	VARIABLE = ARGUMENT,
	/// <summary>Shape flag indicating a single-row structured type.</summary>
	OBJECT = 1 << 6,
	/// <summary>Shape flag indicating a multi-row table type.</summary>
	TABLE = 1 << 7,
	/// <summary>Unrecognized or unset block type.</summary>
	INVALID = 0
};
