using System.ComponentModel.Composition;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Classification;
using Microsoft.VisualStudio.Utilities;
using SQuiL.VisualStudioExtension.ContentType;

namespace SQuiL.VisualStudioExtension.Classification;

/// <summary>
/// MEF-exported provider for <see cref="SQuiLLinkedKeyClassifier"/> (Task 16
/// relationship-key coloring). A SECOND <see cref="IClassifierProvider"/> for
/// the same content type as <see cref="SQuiLClassifierProvider"/> — the host
/// aggregates every classifier registered for a buffer's content type (the
/// same mechanism that already layers our <see cref="SQuiLClassifier"/> on
/// top of the host's own SQL classifier), so this rides alongside both
/// without disturbing either.
/// </summary>
[Export(typeof(IClassifierProvider))]
[ContentType(SQuiLContentTypeDefinition.OverlayContentTypeName)]
internal sealed class SQuiLLinkedKeyClassifierProvider : IClassifierProvider
{
    [Import] internal IClassificationTypeRegistryService ClassificationRegistry = null!;

    public IClassifier? GetClassifier(ITextBuffer buffer)
    {
        if (!SQuiLContentTypeDefinition.IsSquilBuffer(buffer)) return null;
        return buffer.Properties.GetOrCreateSingletonProperty(
            () => new SQuiLLinkedKeyClassifier(buffer, ClassificationRegistry));
    }
}
