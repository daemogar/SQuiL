using System.ComponentModel.Composition;
using System.Windows.Media;
using Microsoft.VisualStudio.Text.Classification;
using Microsoft.VisualStudio.Utilities;

namespace SQuiL.VisualStudioExtension.Classification;

// ╔══════════════════════════════════════════════════════════════════════════╗
// ║  Default colours for each SQuiL classification span.                     ║
// ║                                                                          ║
// ║  Colours are picked to be readable against both the standard SSMS light  ║
// ║  and dark themes.  Users can override any of them through Tools → Options║
// ║  → Environment → Fonts and Colors → "SQuiL …".                           ║
// ║                                                                          ║
// ║  Pattern: one [ClassificationType]/[Export] pair per span, paired with a ║
// ║  ClassificationTypeDefinition above in SQuiLClassificationTypes.         ║
// ╚══════════════════════════════════════════════════════════════════════════╝

internal static class SQuiLColours
{
    // SQuiL-specific palette — distinct from the SQL-keyword default blue
    // so users can tell at a glance which @variables map to generated code.
    // The Param colour was originally teal, which collided with the Type
    // colour (varchar/int) when our classifier began layering on top of
    // SSMS's SQL classifier.  Switched Param to a saturated coral to read
    // clearly against teal types.
    public static readonly Color Param     = Color.FromRgb(0xFF, 0x9E, 0x64); // coral
    public static readonly Color Return    = Color.FromRgb(0xC5, 0x86, 0xC0); // mauve
    public static readonly Color Special   = Color.FromRgb(0xDC, 0xDC, 0xAA); // pale yellow
    public static readonly Color OtherVar  = Color.FromRgb(0x9C, 0xDC, 0xFE); // sky blue

    public static readonly Color Use       = Color.FromRgb(0xCE, 0x91, 0x78); // burnt orange
    public static readonly Color Database  = Color.FromRgb(0x4F, 0xC1, 0xFF); // bright blue

    public static readonly Color SqlKwd    = Color.FromRgb(0x56, 0x9C, 0xD6); // sql-blue
    public static readonly Color Ddl       = Color.FromRgb(0xC5, 0x86, 0xC0);
    public static readonly Color Control   = Color.FromRgb(0xD8, 0xA0, 0xDF);
    public static readonly Color Function  = Color.FromRgb(0xDC, 0xDC, 0xAA);
    public static readonly Color Type      = Color.FromRgb(0x4E, 0xC9, 0xB0); // teal (unchanged)
    public static readonly Color Constant  = Color.FromRgb(0xB5, 0xCE, 0xA8);
    public static readonly Color Operator  = Color.FromRgb(0xD4, 0xD4, 0xD4);
    public static readonly Color BracketId = Color.FromRgb(0xC8, 0xC8, 0xC8);

    public static readonly Color Annotation= Color.FromRgb(0x57, 0xA6, 0x4A); // green
    public static readonly Color Comment   = Color.FromRgb(0x6A, 0x99, 0x55);
    public static readonly Color StringLit = Color.FromRgb(0xCE, 0x91, 0x78);
    public static readonly Color Number    = Color.FromRgb(0xB5, 0xCE, 0xA8);
}

// ── @Param_*, @Params_* ──────────────────────────────────────────────────
[Export(typeof(EditorFormatDefinition))]
[ClassificationType(ClassificationTypeNames = SQuiLClassificationTypes.SQuiLParamVariable)]
[Name(SQuiLClassificationTypes.SQuiLParamVariable)]
[UserVisible(true)]
[Order(After = Priority.Default)]
internal sealed class SQuiLParamVariableFormat : ClassificationFormatDefinition
{
    public SQuiLParamVariableFormat()
    {
        DisplayName     = "SQuiL Param Variable";
        ForegroundColor = SQuiLColours.Param;
    }
}

// ── @Return_*, @Returns_* ────────────────────────────────────────────────
[Export(typeof(EditorFormatDefinition))]
[ClassificationType(ClassificationTypeNames = SQuiLClassificationTypes.SQuiLReturnVariable)]
[Name(SQuiLClassificationTypes.SQuiLReturnVariable)]
[UserVisible(true)]
[Order(After = Priority.Default)]
internal sealed class SQuiLReturnVariableFormat : ClassificationFormatDefinition
{
    public SQuiLReturnVariableFormat()
    {
        DisplayName     = "SQuiL Return Variable";
        ForegroundColor = SQuiLColours.Return;
    }
}

// ── @Debug, @EnvironmentName ────────────────────────────────────────────
[Export(typeof(EditorFormatDefinition))]
[ClassificationType(ClassificationTypeNames = SQuiLClassificationTypes.SQuiLSpecialVariable)]
[Name(SQuiLClassificationTypes.SQuiLSpecialVariable)]
[UserVisible(true)]
[Order(After = Priority.Default)]
internal sealed class SQuiLSpecialVariableFormat : ClassificationFormatDefinition
{
    public SQuiLSpecialVariableFormat()
    {
        DisplayName     = "SQuiL Special Variable";
        ForegroundColor = SQuiLColours.Special;
        IsItalic        = true;
    }
}

// ── any other @ident ─────────────────────────────────────────────────────
[Export(typeof(EditorFormatDefinition))]
[ClassificationType(ClassificationTypeNames = SQuiLClassificationTypes.SQuiLOtherVariable)]
[Name(SQuiLClassificationTypes.SQuiLOtherVariable)]
[UserVisible(true)]
[Order(After = Priority.Default)]
internal sealed class SQuiLOtherVariableFormat : ClassificationFormatDefinition
{
    public SQuiLOtherVariableFormat()
    {
        DisplayName     = "SQuiL Other Variable";
        ForegroundColor = SQuiLColours.OtherVar;
    }
}

// ── USE keyword ──────────────────────────────────────────────────────────
[Export(typeof(EditorFormatDefinition))]
[ClassificationType(ClassificationTypeNames = SQuiLClassificationTypes.SQuiLUseKeyword)]
[Name(SQuiLClassificationTypes.SQuiLUseKeyword)]
[UserVisible(true)]
[Order(After = Priority.Default)]
internal sealed class SQuiLUseKeywordFormat : ClassificationFormatDefinition
{
    public SQuiLUseKeywordFormat()
    {
        DisplayName     = "SQuiL USE Keyword";
        ForegroundColor = SQuiLColours.Use;
        IsBold          = true;
    }
}

// ── database name inside USE ─────────────────────────────────────────────
[Export(typeof(EditorFormatDefinition))]
[ClassificationType(ClassificationTypeNames = SQuiLClassificationTypes.SQuiLDatabaseName)]
[Name(SQuiLClassificationTypes.SQuiLDatabaseName)]
[UserVisible(true)]
[Order(After = Priority.Default)]
internal sealed class SQuiLDatabaseNameFormat : ClassificationFormatDefinition
{
    public SQuiLDatabaseNameFormat()
    {
        DisplayName     = "SQuiL Database Name";
        ForegroundColor = SQuiLColours.Database;
    }
}

// ── DECLARE keyword ──────────────────────────────────────────────────────
[Export(typeof(EditorFormatDefinition))]
[ClassificationType(ClassificationTypeNames = SQuiLClassificationTypes.SQuiLDeclareKeyword)]
[Name(SQuiLClassificationTypes.SQuiLDeclareKeyword)]
[UserVisible(true)]
[Order(After = Priority.Default)]
internal sealed class SQuiLDeclareKeywordFormat : ClassificationFormatDefinition
{
    public SQuiLDeclareKeywordFormat()
    {
        DisplayName     = "SQuiL DECLARE Keyword";
        ForegroundColor = SQuiLColours.SqlKwd;
        IsBold          = true;
    }
}

// ── DML keywords (Select/Insert/Update/…) ────────────────────────────────
[Export(typeof(EditorFormatDefinition))]
[ClassificationType(ClassificationTypeNames = SQuiLClassificationTypes.SQuiLDmlKeyword)]
[Name(SQuiLClassificationTypes.SQuiLDmlKeyword)]
[UserVisible(true)]
[Order(After = Priority.Default)]
internal sealed class SQuiLDmlKeywordFormat : ClassificationFormatDefinition
{
    public SQuiLDmlKeywordFormat()
    {
        DisplayName     = "SQuiL DML Keyword";
        ForegroundColor = SQuiLColours.SqlKwd;
    }
}

// ── DDL keywords ─────────────────────────────────────────────────────────
[Export(typeof(EditorFormatDefinition))]
[ClassificationType(ClassificationTypeNames = SQuiLClassificationTypes.SQuiLDdlKeyword)]
[Name(SQuiLClassificationTypes.SQuiLDdlKeyword)]
[UserVisible(true)]
[Order(After = Priority.Default)]
internal sealed class SQuiLDdlKeywordFormat : ClassificationFormatDefinition
{
    public SQuiLDdlKeywordFormat()
    {
        DisplayName     = "SQuiL DDL Keyword";
        ForegroundColor = SQuiLColours.Ddl;
    }
}

// ── Control keywords (If/Else/Begin/…) ───────────────────────────────────
[Export(typeof(EditorFormatDefinition))]
[ClassificationType(ClassificationTypeNames = SQuiLClassificationTypes.SQuiLControlKeyword)]
[Name(SQuiLClassificationTypes.SQuiLControlKeyword)]
[UserVisible(true)]
[Order(After = Priority.Default)]
internal sealed class SQuiLControlKeywordFormat : ClassificationFormatDefinition
{
    public SQuiLControlKeywordFormat()
    {
        DisplayName     = "SQuiL Control Keyword";
        ForegroundColor = SQuiLColours.Control;
    }
}

// ── Built-in functions ───────────────────────────────────────────────────
[Export(typeof(EditorFormatDefinition))]
[ClassificationType(ClassificationTypeNames = SQuiLClassificationTypes.SQuiLFunctionKeyword)]
[Name(SQuiLClassificationTypes.SQuiLFunctionKeyword)]
[UserVisible(true)]
[Order(After = Priority.Default)]
internal sealed class SQuiLFunctionKeywordFormat : ClassificationFormatDefinition
{
    public SQuiLFunctionKeywordFormat()
    {
        DisplayName     = "SQuiL Function";
        ForegroundColor = SQuiLColours.Function;
    }
}

// ── SQL types (int/varchar/…) ────────────────────────────────────────────
// Bumped to Priority.High so our teal wins over SSMS's "SQL keyword" blue
// for int/varchar/bigint/uniqueidentifier/etc.  We DELIBERATELY do not
// override the SQL classifier on plain keywords (SELECT/FROM/WHERE/…) —
// only on type identifiers — so the visual distinction the user wanted
// (types ≠ keywords) actually lands.
[Export(typeof(EditorFormatDefinition))]
[ClassificationType(ClassificationTypeNames = SQuiLClassificationTypes.SQuiLSqlType)]
[Name(SQuiLClassificationTypes.SQuiLSqlType)]
[UserVisible(true)]
[Order(After = Priority.High)]
internal sealed class SQuiLSqlTypeFormat : ClassificationFormatDefinition
{
    public SQuiLSqlTypeFormat()
    {
        DisplayName     = "SQuiL SQL Type";
        ForegroundColor = SQuiLColours.Type;
    }
}

// ── Constants (NULL/TRUE/FALSE) ──────────────────────────────────────────
[Export(typeof(EditorFormatDefinition))]
[ClassificationType(ClassificationTypeNames = SQuiLClassificationTypes.SQuiLConstant)]
[Name(SQuiLClassificationTypes.SQuiLConstant)]
[UserVisible(true)]
[Order(After = Priority.Default)]
internal sealed class SQuiLConstantFormat : ClassificationFormatDefinition
{
    public SQuiLConstantFormat()
    {
        DisplayName     = "SQuiL Constant";
        ForegroundColor = SQuiLColours.Constant;
    }
}

// ── Operators ────────────────────────────────────────────────────────────
[Export(typeof(EditorFormatDefinition))]
[ClassificationType(ClassificationTypeNames = SQuiLClassificationTypes.SQuiLOperator)]
[Name(SQuiLClassificationTypes.SQuiLOperator)]
[UserVisible(true)]
[Order(After = Priority.Default)]
internal sealed class SQuiLOperatorFormat : ClassificationFormatDefinition
{
    public SQuiLOperatorFormat()
    {
        DisplayName     = "SQuiL Operator";
        ForegroundColor = SQuiLColours.Operator;
    }
}

// ── [BracketIdentifier] ──────────────────────────────────────────────────
[Export(typeof(EditorFormatDefinition))]
[ClassificationType(ClassificationTypeNames = SQuiLClassificationTypes.SQuiLBracketId)]
[Name(SQuiLClassificationTypes.SQuiLBracketId)]
[UserVisible(true)]
[Order(After = Priority.Default)]
internal sealed class SQuiLBracketIdFormat : ClassificationFormatDefinition
{
    public SQuiLBracketIdFormat()
    {
        DisplayName     = "SQuiL Bracket Identifier";
        ForegroundColor = SQuiLColours.BracketId;
    }
}

// ── --Name: annotation ───────────────────────────────────────────────────
[Export(typeof(EditorFormatDefinition))]
[ClassificationType(ClassificationTypeNames = SQuiLClassificationTypes.SQuiLNameAnnotation)]
[Name(SQuiLClassificationTypes.SQuiLNameAnnotation)]
[UserVisible(true)]
[Order(After = Priority.Default)]
internal sealed class SQuiLNameAnnotationFormat : ClassificationFormatDefinition
{
    public SQuiLNameAnnotationFormat()
    {
        DisplayName     = "SQuiL --Name: Annotation";
        ForegroundColor = SQuiLColours.Annotation;
        IsBold          = true;
    }
}

// ── line / block comments ────────────────────────────────────────────────
[Export(typeof(EditorFormatDefinition))]
[ClassificationType(ClassificationTypeNames = SQuiLClassificationTypes.SQuiLLineComment)]
[Name(SQuiLClassificationTypes.SQuiLLineComment)]
[UserVisible(true)]
[Order(After = Priority.Default)]
internal sealed class SQuiLLineCommentFormat : ClassificationFormatDefinition
{
    public SQuiLLineCommentFormat()
    {
        DisplayName     = "SQuiL Line Comment";
        ForegroundColor = SQuiLColours.Comment;
    }
}

[Export(typeof(EditorFormatDefinition))]
[ClassificationType(ClassificationTypeNames = SQuiLClassificationTypes.SQuiLBlockComment)]
[Name(SQuiLClassificationTypes.SQuiLBlockComment)]
[UserVisible(true)]
[Order(After = Priority.Default)]
internal sealed class SQuiLBlockCommentFormat : ClassificationFormatDefinition
{
    public SQuiLBlockCommentFormat()
    {
        DisplayName     = "SQuiL Block Comment";
        ForegroundColor = SQuiLColours.Comment;
    }
}

// ── String / Number literals ─────────────────────────────────────────────
[Export(typeof(EditorFormatDefinition))]
[ClassificationType(ClassificationTypeNames = SQuiLClassificationTypes.SQuiLString)]
[Name(SQuiLClassificationTypes.SQuiLString)]
[UserVisible(true)]
[Order(After = Priority.Default)]
internal sealed class SQuiLStringFormat : ClassificationFormatDefinition
{
    public SQuiLStringFormat()
    {
        DisplayName     = "SQuiL String Literal";
        ForegroundColor = SQuiLColours.StringLit;
    }
}

[Export(typeof(EditorFormatDefinition))]
[ClassificationType(ClassificationTypeNames = SQuiLClassificationTypes.SQuiLNumber)]
[Name(SQuiLClassificationTypes.SQuiLNumber)]
[UserVisible(true)]
[Order(After = Priority.Default)]
internal sealed class SQuiLNumberFormat : ClassificationFormatDefinition
{
    public SQuiLNumberFormat()
    {
        DisplayName     = "SQuiL Number Literal";
        ForegroundColor = SQuiLColours.Number;
    }
}
