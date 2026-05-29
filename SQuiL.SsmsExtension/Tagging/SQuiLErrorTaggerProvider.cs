using System.ComponentModel.Composition;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Tagging;
using Microsoft.VisualStudio.Utilities;
using SQuiL.SsmsExtension.ContentType;

namespace SQuiL.SsmsExtension.Tagging;

/// <summary>
/// MEF-exported tagger provider that gives SSMS error squiggles for every
/// .squil buffer.  Subscribes to the broader <c>SQL</c> content type so the
/// SSMS SQL editor buffers are queried, and gates by file extension via
/// <see cref="SQuiLContentTypeDefinition.IsSquilBuffer"/>.
/// </summary>
[Export(typeof(ITaggerProvider))]
[ContentType(SQuiLContentTypeDefinition.SqlContentTypeName)]
[TagType(typeof(IErrorTag))]
[Name("SQuiL Error Tagger")]
internal sealed class SQuiLErrorTaggerProvider : ITaggerProvider
{
    public ITagger<T>? CreateTagger<T>(ITextBuffer buffer) where T : ITag
    {
        if (!SQuiLContentTypeDefinition.IsSquilBuffer(buffer)) return null;
        return buffer.Properties.GetOrCreateSingletonProperty(
            () => new SQuiLErrorTagger(buffer)) as ITagger<T>;
    }
}
