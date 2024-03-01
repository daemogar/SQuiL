using Microsoft.CodeAnalysis;

using SquilParser.SourceGenerator.Parser;

using System.Collections.Immutable;

namespace SQuiL.Models;

public class SQuiLObject(
	string NameSpace,
	string Modifiers,
	string Type,
	CodeBlock Block,
	ImmutableDictionary<string, SQuiLTableMap> TableMap,
	ImmutableDictionary<string, Generator.SQuiLPartialModel> Records)
	: SQuiLTable(NameSpace, Modifiers, Type, Block, TableMap, Records)
{ }
