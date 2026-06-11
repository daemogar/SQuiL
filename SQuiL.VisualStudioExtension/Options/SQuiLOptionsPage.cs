using System.ComponentModel;
using Microsoft.VisualStudio.Shell;

namespace SQuiL.VisualStudioExtension.Options;

/// <summary>
/// Tools › Options › SQuiL. Only setting today is whether the extension checks
/// GitHub for newer releases on load. The "Check for Updates" command ignores
/// this and always runs.
/// </summary>
public sealed class SQuiLOptionsPage : DialogPage
{
    [Category("Updates")]
    [DisplayName("Check for updates")]
    [Description("Automatically check GitHub for newer SQuiL extension releases when Visual Studio starts.")]
    public bool EnableUpdateCheck { get; set; } = true;
}
