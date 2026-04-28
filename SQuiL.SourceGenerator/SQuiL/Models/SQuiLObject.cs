using Microsoft.CodeAnalysis;

using SQuiL.SourceGenerator.Parser;

using System.Collections.Immutable;

namespace SQuiL.Models;

/// <summary>
/// Represents a SQL input parameter declared as a single-row structured type
/// (e.g. <c>@Param_Name table(...)</c>) whose C# counterpart is emitted as an object record
/// rather than a list.  Extends <see cref="SQuiLTable"/> but signals object (not collection) semantics.
/// </summary>
/// <param name="NameSpace">The C# namespace the generated record will be emitted into.</param>
/// <param name="Modifiers">The C# access and type modifiers for the record.</param>
/// <param name="Type">The type-name suffix appended when building the record name.</param>
/// <param name="Block">The parsed SQL code block that defines this object's columns and metadata.</param>
/// <param name="TableMap">The shared table-name-to-C#-type mapping.</param>
/// <param name="Records">All partial record declarations visible in the compilation.</param>
public class SQuiLObject(
	string NameSpace,
	string Modifiers,
	string Type,
	CodeBlock Block,
	SQuiLTableMap TableMap,
	ImmutableDictionary<string, Generator.SQuiLPartialModel> Records)
	: SQuiLTable(NameSpace, Modifiers, Type, Block, TableMap, Records)
{ }
