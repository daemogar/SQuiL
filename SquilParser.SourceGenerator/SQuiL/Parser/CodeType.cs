namespace SquilParser.SourceGenerator.Parser;

[Flags]
public enum CodeType
{
	USING = 1,
	BODY = 2,
	INPUT = 1 << 2,
	INPUT_ARGUMENT = INPUT | ARGUMENT,
	INPUT_TABLE = INPUT | TABLE,
	OUTPUT = 1 << 3,
	OUTPUT_VARIABLE = OUTPUT | VARIABLE,
	OUTPUT_TABLE = OUTPUT | TABLE,
	ARGUMENT = 1 << 4,
	VARIABLE = ARGUMENT,
	TABLE = 1 << 5,
	INVALID = 0
};
