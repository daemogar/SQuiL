using System.Runtime.InteropServices;
using Microsoft.VisualStudio.Shell;

namespace SQuiL.SsmsExtension.Guide;

/// <summary>
/// SQuiL writing guide tool window.  Pane host for
/// <see cref="SQuiLGuideToolWindowControl"/>.
///
/// Registered with the package via
/// <c>[ProvideToolWindow(typeof(SQuiLGuideToolWindow))]</c> on
/// <see cref="SQuiLPackage"/>, and shown either through the View → Other
/// Windows → SQuiL Writing Guide menu entry or by the first-run auto-open
/// in <see cref="SQuiLPackage.InitializeAsync"/>.
/// </summary>
[Guid(SQuiLPackageGuids.GuideToolWindowGuidString)]
public sealed class SQuiLGuideToolWindow : ToolWindowPane
{
    public SQuiLGuideToolWindow() : base(null)
    {
        Caption = "SQuiL Writing Guide";
        Content = new SQuiLGuideToolWindowControl();
    }
}
