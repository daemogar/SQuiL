using System.ComponentModel.Composition;
using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Operations;
using Microsoft.VisualStudio.Utilities;
using SQuiL.SsmsExtension.ContentType;

namespace SQuiL.SsmsExtension.Completion;

/// <summary>
/// MEF-exported source provider that gives SSMS a SQuiL-specific
/// <see cref="ICompletionSource"/> for every .squil buffer.  We subscribe
/// to the broader <c>SQL</c> content type (because SSMS's SQL Query Editor
/// pins that on every .sql/.squil buffer) and gate on file extension via
/// <see cref="SQuiLContentTypeDefinition.IsSquilBuffer"/>.
/// </summary>
[Export(typeof(ICompletionSourceProvider))]
[ContentType(SQuiLContentTypeDefinition.SqlContentTypeName)]
[Name("SQuiL Completion")]
internal sealed class SQuiLCompletionSourceProvider : ICompletionSourceProvider
{
    [Import] internal ITextStructureNavigatorSelectorService NavigatorService = null!;
    [Import] internal IGlyphService GlyphService = null!;

    public ICompletionSource? TryCreateCompletionSource(ITextBuffer buffer)
    {
        if (!SQuiLContentTypeDefinition.IsSquilBuffer(buffer)) return null;
        return buffer.Properties.GetOrCreateSingletonProperty(
            () => (ICompletionSource)new SQuiLCompletionSource(this, buffer));
    }
}
