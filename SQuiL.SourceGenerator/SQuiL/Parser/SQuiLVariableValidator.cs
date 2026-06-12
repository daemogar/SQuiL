namespace SQuiL.SourceGenerator.Parser;

/// <summary>
/// Validates <c>@variable</c> usage in a SQuiL file:
/// <list type="bullet">
/// <item>Every <c>@variable</c> reference must be preceded by a <c>DECLARE</c> for that
/// exact name — the same rule SQL Server enforces at batch compile time ("Must declare
/// the scalar variable"). SQuiL performs no name remapping: a file that references an
/// undeclared variable is invalid SQL. This applies to every variable including
/// <c>@Debug</c> and <c>@EnvironmentName</c>.</item>
/// <item><c>@Debug</c> and <c>@EnvironmentName</c> must be declared before the
/// <c>USE</c> statement (in the header), and preferably before any other declaration.</item>
/// </list>
/// <para>
/// Pure logic with no Roslyn dependencies — mirrored by <c>lintUndeclaredVariables</c>
/// in the VS Code extension (<c>parser.ts</c>) and <c>LintUndeclaredVariables</c> in the
/// SSMS extension (<c>SQuiLLinter.cs</c>). Change one, change the others.
/// </para>
/// </summary>
public static class SQuiLVariableValidator
{
	/// <summary>Classifies a <see cref="Finding"/>.</summary>
	public enum FindingKind
	{
		/// <summary>The variable is referenced but never declared. Error.</summary>
		Undeclared,
		/// <summary>The variable is referenced before its declaration. Error.</summary>
		UsedBeforeDeclared,
		/// <summary>@Debug/@EnvironmentName is declared after the USE statement. Error.</summary>
		SpecialAfterUse,
		/// <summary>@Debug/@EnvironmentName is declared in the header but other declarations precede it. Warning.</summary>
		SpecialNotFirst,
	}

	/// <summary>One validation finding in the SQL text.</summary>
	/// <param name="Kind">What rule the finding violates.</param>
	/// <param name="Name">The variable, including the <c>@</c>.</param>
	/// <param name="Line">1-based line of the reference/declaration.</param>
	/// <param name="Column">1-based column of the reference/declaration.</param>
	public sealed record Finding(FindingKind Kind, string Name, int Line, int Column);

	private enum State { Normal, ExpectVariable, InType, InDefault }

	private static readonly HashSet<string> StatementStarters = new(StringComparer.OrdinalIgnoreCase)
	{
		"SELECT", "INSERT", "UPDATE", "DELETE", "SET", "IF", "WHILE", "BEGIN", "END",
		"USE", "DECLARE", "EXEC", "EXECUTE", "WITH", "MERGE", "PRINT", "RETURN",
		"CREATE", "DROP", "ALTER", "TRUNCATE", "GO",
	};

	private static bool IsSpecial(string name)
		=> name.Equals("@Debug", StringComparison.OrdinalIgnoreCase)
		|| name.Equals("@EnvironmentName", StringComparison.OrdinalIgnoreCase);

	/// <summary>
	/// Scans <paramref name="sql"/> and returns every rule violation in document order.
	/// </summary>
	public static List<Finding> Validate(string sql)
	{
		var text = Mask(sql);

		List<(string Name, int Offset)> declarations = [];
		List<(string Name, int Offset)> references = [];
		int? useOffset = null;

		var state = State.Normal;
		var parenDepth = 0;
		var caseDepth = 0;
		var i = 0;

		while (i < text.Length)
		{
			var c = text[i];

			if (c == '(') { parenDepth++; i++; continue; }
			if (c == ')') { if (parenDepth > 0) parenDepth--; i++; continue; }

			if (c == ';')
			{
				if (parenDepth == 0) { state = State.Normal; caseDepth = 0; }
				i++;
				continue;
			}

			if (c == ',')
			{
				if (parenDepth == 0 && (state == State.InType || state == State.InDefault))
					state = State.ExpectVariable;
				i++;
				continue;
			}

			if (c == '=')
			{
				if (parenDepth == 0 && state == State.InType)
					state = State.InDefault;
				i++;
				continue;
			}

			if (c == '@')
			{
				var start = i;
				i++;
				if (i < text.Length && text[i] == '@')
				{
					// system variable (@@ROWCOUNT etc.) — skip the whole token
					i++;
					while (i < text.Length && IsNameChar(text[i])) i++;
					continue;
				}

				var nameStart = i;
				while (i < text.Length && IsNameChar(text[i])) i++;
				if (i == nameStart) continue; // a lone '@' is not a variable

				var name = text.Substring(start, i - start);

				if (state == State.ExpectVariable)
				{
					declarations.Add((name, start));
					state = State.InType;
				}
				else
				{
					references.Add((name, start));
				}
				continue;
			}

			if (char.IsLetter(c) || c == '_')
			{
				var start = i;
				while (i < text.Length && IsNameChar(text[i])) i++;
				var word = text.Substring(start, i - start);

				if (word.Equals("DECLARE", StringComparison.OrdinalIgnoreCase))
				{
					state = State.ExpectVariable;
					continue;
				}

				if (state == State.Normal && useOffset is null
					&& word.Equals("USE", StringComparison.OrdinalIgnoreCase)
					&& parenDepth == 0)
				{
					useOffset = start;
					continue;
				}

				// CASE…END pairs inside a default-value expression must not end the
				// declare statement when END is reached.
				if (state == State.InDefault && word.Equals("CASE", StringComparison.OrdinalIgnoreCase))
				{
					caseDepth++;
					continue;
				}
				if (state == State.InDefault && caseDepth > 0 && word.Equals("END", StringComparison.OrdinalIgnoreCase))
				{
					caseDepth--;
					continue;
				}

				if (parenDepth == 0
					&& (state == State.InType || state == State.InDefault)
					&& StatementStarters.Contains(word))
				{
					state = State.Normal;
					caseDepth = 0;
					// no semicolon between the declare and the next statement —
					// re-read the word in Normal state so DECLARE/USE chains work
					i = start;
				}
				continue;
			}

			i++;
		}

		List<(int Offset, Finding Finding)> findings = [];

		foreach (var (name, offset) in references)
		{
			var declaredBefore = false;
			var declaredAnywhere = false;
			foreach (var (declared, declaredOffset) in declarations)
			{
				if (!declared.Equals(name, StringComparison.OrdinalIgnoreCase)) continue;
				declaredAnywhere = true;
				if (declaredOffset < offset) { declaredBefore = true; break; }
			}

			if (declaredBefore) continue;

			var (line, column) = Position(sql, offset);
			findings.Add((offset, new(
				declaredAnywhere ? FindingKind.UsedBeforeDeclared : FindingKind.Undeclared,
				name, line, column)));
		}

		foreach (var (name, offset) in declarations)
		{
			if (!IsSpecial(name)) continue;

			if (useOffset is int use && offset > use)
			{
				var (line, column) = Position(sql, offset);
				findings.Add((offset, new(FindingKind.SpecialAfterUse, name, line, column)));
				continue;
			}

			foreach (var (other, otherOffset) in declarations)
			{
				if (otherOffset >= offset || IsSpecial(other)) continue;

				var (line, column) = Position(sql, offset);
				findings.Add((offset, new(FindingKind.SpecialNotFirst, name, line, column)));
				break;
			}
		}

		return [.. findings.OrderBy(f => f.Offset).Select(f => f.Finding)];
	}

	private static bool IsNameChar(char c)
		=> char.IsLetterOrDigit(c) || c == '_' || c == '$' || c == '#';

	/// <summary>
	/// Replaces comments (line and nested block), string literals, and bracketed
	/// identifiers with spaces so the scanner never sees their contents. Offsets
	/// and newlines are preserved.
	/// </summary>
	private static string Mask(string sql)
	{
		var chars = sql.ToCharArray();
		var i = 0;

		while (i < chars.Length)
		{
			var c = chars[i];

			if (c == '-' && i + 1 < chars.Length && chars[i + 1] == '-')
			{
				while (i < chars.Length && chars[i] != '\n') chars[i++] = ' ';
				continue;
			}

			if (c == '/' && i + 1 < chars.Length && chars[i + 1] == '*')
			{
				var depth = 0;
				while (i < chars.Length)
				{
					if (chars[i] == '/' && i + 1 < chars.Length && chars[i + 1] == '*')
					{
						depth++;
						chars[i] = ' '; chars[i + 1] = ' ';
						i += 2;
						continue;
					}
					if (chars[i] == '*' && i + 1 < chars.Length && chars[i + 1] == '/')
					{
						depth--;
						chars[i] = ' '; chars[i + 1] = ' ';
						i += 2;
						if (depth == 0) break;
						continue;
					}
					if (chars[i] != '\n' && chars[i] != '\r') chars[i] = ' ';
					i++;
				}
				continue;
			}

			if (c == '\'')
			{
				chars[i++] = ' ';
				while (i < chars.Length)
				{
					if (chars[i] == '\'')
					{
						if (i + 1 < chars.Length && chars[i + 1] == '\'')
						{
							chars[i] = ' '; chars[i + 1] = ' ';
							i += 2;
							continue;
						}
						chars[i++] = ' ';
						break;
					}
					if (chars[i] != '\n' && chars[i] != '\r') chars[i] = ' ';
					i++;
				}
				continue;
			}

			if (c == '[')
			{
				while (i < chars.Length && chars[i] != ']') chars[i++] = ' ';
				if (i < chars.Length) chars[i++] = ' ';
				continue;
			}

			i++;
		}

		return new string(chars);
	}

	private static (int Line, int Column) Position(string sql, int offset)
	{
		var line = 1;
		var column = 1;
		for (var i = 0; i < offset && i < sql.Length; i++)
		{
			if (sql[i] == '\n') { line++; column = 1; }
			else column++;
		}
		return (line, column);
	}
}
