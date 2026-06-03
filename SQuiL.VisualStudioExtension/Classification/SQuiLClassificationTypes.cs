using System.ComponentModel.Composition;
using Microsoft.VisualStudio.Text.Classification;
using Microsoft.VisualStudio.Utilities;

namespace SQuiL.VisualStudioExtension.Classification;

/// <summary>
/// MEF-exported <see cref="ClassificationTypeDefinition"/>s for every span
/// the SQuiL classifier produces.  These names must match the
/// <see cref="ClassificationTypeAttribute.ClassificationTypeNames"/> values
/// used on the corresponding <see cref="EditorFormatDefinition"/>s in
/// <see cref="SQuiLClassificationFormats"/>.
///
/// The set mirrors the scope distinctions in
/// <c>SQuiL.Editor.Shared\squil.tmLanguage.json</c> so the SSMS experience
/// matches VS Code.  The same SQuiL roles (@Param_*, @Return_*, special vars)
/// are coloured distinctly from plain SQL keywords; the USE statement gets
/// its own scope so the database name reads as an entity, not a keyword.
/// </summary>
internal static class SQuiLClassificationTypes
{
    public const string SQuiLParamVariable    = "squil.param-variable";
    public const string SQuiLReturnVariable   = "squil.return-variable";
    public const string SQuiLSpecialVariable  = "squil.special-variable";
    public const string SQuiLOtherVariable    = "squil.other-variable";

    public const string SQuiLUseKeyword       = "squil.use-keyword";
    public const string SQuiLDatabaseName     = "squil.database-name";
    public const string SQuiLDeclareKeyword   = "squil.declare-keyword";

    public const string SQuiLDmlKeyword       = "squil.dml-keyword";
    public const string SQuiLDdlKeyword       = "squil.ddl-keyword";
    public const string SQuiLControlKeyword   = "squil.control-keyword";
    public const string SQuiLFunctionKeyword  = "squil.function-keyword";
    public const string SQuiLSqlType          = "squil.sql-type";
    public const string SQuiLConstant         = "squil.constant";
    public const string SQuiLOperator         = "squil.operator";
    public const string SQuiLBracketId        = "squil.bracket-identifier";

    public const string SQuiLNameAnnotation   = "squil.name-annotation";
    public const string SQuiLLineComment      = "squil.line-comment";
    public const string SQuiLBlockComment     = "squil.block-comment";
    public const string SQuiLString           = "squil.string";
    public const string SQuiLNumber           = "squil.number";

    // ── Exports (one per classification type) ──────────────────────────────

    [Export] [Name(SQuiLParamVariable)]   internal static ClassificationTypeDefinition ParamVariable   { get; } = null!;
    [Export] [Name(SQuiLReturnVariable)]  internal static ClassificationTypeDefinition ReturnVariable  { get; } = null!;
    [Export] [Name(SQuiLSpecialVariable)] internal static ClassificationTypeDefinition SpecialVariable { get; } = null!;
    [Export] [Name(SQuiLOtherVariable)]   internal static ClassificationTypeDefinition OtherVariable   { get; } = null!;

    [Export] [Name(SQuiLUseKeyword)]      internal static ClassificationTypeDefinition UseKeyword      { get; } = null!;
    [Export] [Name(SQuiLDatabaseName)]    internal static ClassificationTypeDefinition DatabaseName    { get; } = null!;
    [Export] [Name(SQuiLDeclareKeyword)]  internal static ClassificationTypeDefinition DeclareKeyword  { get; } = null!;

    [Export] [Name(SQuiLDmlKeyword)]      internal static ClassificationTypeDefinition DmlKeyword      { get; } = null!;
    [Export] [Name(SQuiLDdlKeyword)]      internal static ClassificationTypeDefinition DdlKeyword      { get; } = null!;
    [Export] [Name(SQuiLControlKeyword)]  internal static ClassificationTypeDefinition ControlKeyword  { get; } = null!;
    [Export] [Name(SQuiLFunctionKeyword)] internal static ClassificationTypeDefinition FunctionKeyword { get; } = null!;
    [Export] [Name(SQuiLSqlType)]         internal static ClassificationTypeDefinition SqlType         { get; } = null!;
    [Export] [Name(SQuiLConstant)]        internal static ClassificationTypeDefinition Constant        { get; } = null!;
    [Export] [Name(SQuiLOperator)]        internal static ClassificationTypeDefinition Operator        { get; } = null!;
    [Export] [Name(SQuiLBracketId)]       internal static ClassificationTypeDefinition BracketId       { get; } = null!;

    [Export] [Name(SQuiLNameAnnotation)]  internal static ClassificationTypeDefinition NameAnnotation  { get; } = null!;
    [Export] [Name(SQuiLLineComment)]     internal static ClassificationTypeDefinition LineComment     { get; } = null!;
    [Export] [Name(SQuiLBlockComment)]    internal static ClassificationTypeDefinition BlockComment    { get; } = null!;
    [Export] [Name(SQuiLString)]          internal static ClassificationTypeDefinition String          { get; } = null!;
    [Export] [Name(SQuiLNumber)]          internal static ClassificationTypeDefinition Number          { get; } = null!;
}
