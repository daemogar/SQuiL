using System.ComponentModel.Composition;
using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Utilities;
using SQuiL.VisualStudioExtension.ContentType;

namespace SQuiL.VisualStudioExtension.QuickInfo;

/// <summary>
/// MEF-exported provider that supplies an <see cref="IAsyncQuickInfoSource"/>
/// to every .squil buffer.  Subscribes to the broader <c>SQL</c> content
/// type (so SSMS's SQL Query Editor buffers get queried) and gates on file
/// extension via <see cref="SQuiLContentTypeDefinition.IsSquilBuffer"/>.
/// </summary>
[Export(typeof(IAsyncQuickInfoSourceProvider))]
[ContentType(SQuiLContentTypeDefinition.OverlayContentTypeName)]
[Name("SQuiL QuickInfo")]
internal sealed class SQuiLQuickInfoSourceProvider : IAsyncQuickInfoSourceProvider
{
    public IAsyncQuickInfoSource? TryCreateQuickInfoSource(ITextBuffer textBuffer)
    {
        if (!SQuiLContentTypeDefinition.IsSquilBuffer(textBuffer)) return null;
        return textBuffer.Properties.GetOrCreateSingletonProperty(
            () => new SQuiLQuickInfoSource(textBuffer));
    }
}
