using System.ComponentModel.Composition;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Classification;
using Microsoft.VisualStudio.Utilities;
using SQuiL.SsmsExtension.ContentType;

namespace SQuiL.SsmsExtension.Classification;

/// <summary>
/// MEF-exported provider that hands SSMS a <see cref="SQuiLClassifier"/> for
/// every .squil buffer.  We subscribe to the broader <c>SQL</c> content type
/// because SSMS's SQL Query Editor pins that on every .sql/.squil buffer;
/// <see cref="SQuiLContentTypeDefinition.IsSquilBuffer"/> short-circuits on
/// regular .sql files so plain SQL gets its native colouring untouched.
///
/// Classifiers cache per buffer via <see cref="PropertyCollection.GetOrCreateSingletonProperty"/>
/// so a single instance services every view on the same buffer.
/// </summary>
[Export(typeof(IClassifierProvider))]
[ContentType(SQuiLContentTypeDefinition.SqlContentTypeName)]
internal sealed class SQuiLClassifierProvider : IClassifierProvider
{
    [Import] internal IClassificationTypeRegistryService ClassificationRegistry = null!;

    public IClassifier? GetClassifier(ITextBuffer buffer)
    {
        if (!SQuiLContentTypeDefinition.IsSquilBuffer(buffer)) return null;
        return buffer.Properties.GetOrCreateSingletonProperty(
            () => new SQuiLClassifier(buffer, ClassificationRegistry));
    }
}
