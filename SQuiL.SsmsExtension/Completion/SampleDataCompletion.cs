using System.Windows.Media;
using Microsoft.VisualStudio.Language.Intellisense;
using SQuiL.SsmsExtension.Parsing;

// `SQuiL.SsmsExtension.Completion` shadows the editor's `Completion` type —
// alias it so we can subclass the right one.
using EditorCompletion = Microsoft.VisualStudio.Language.Intellisense.Completion;

namespace SQuiL.SsmsExtension.Completion;

/// <summary>
/// Marker subclass — a <see cref="Completion"/> that carries the SQuiL
/// variable + "already has a block" flag the
/// <see cref="Commands.InsertSampleDataCommand"/> needs.  When the user
/// accepts this completion, the completion command filter detects the
/// subclass on the committed item and dispatches to the command instead
/// of letting the editor insert plain text.
///
/// <see cref="Completion.InsertionText"/> is deliberately empty so that, if
/// the filter doesn't catch it (e.g. an unusual commit path), the user is
/// not left with a stray "⊕" character in their buffer.
/// </summary>
internal sealed class SampleDataCompletion : EditorCompletion
{
    public SQuiLVariable Variable    { get; }
    public bool          HasExisting { get; }

    public SampleDataCompletion(
        string         displayText,
        string         description,
        ImageSource    iconSource,
        SQuiLVariable  variable,
        bool           hasExisting)
        : base(displayText, insertionText: "", description: description,
               iconSource: iconSource, iconAutomationText: "SQuiL sample data")
    {
        Variable    = variable;
        HasExisting = hasExisting;
    }
}
