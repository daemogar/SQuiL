using System;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using SQuiL.VisualStudioExtension.Commands;
using SQuiL.VisualStudioExtension.Guide;
using Task = System.Threading.Tasks.Task;

namespace SQuiL.VisualStudioExtension;

/// <summary>
/// SQuiL VSPackage — entry point for the SSMS 22.6 extension.
///
/// MEF editor components (classifier, completion, quick-info, taggers) load
/// without this package via the MEF catalog; the package itself owns command
/// registrations, the writing-guide tool window, and the first-run
/// auto-open of that tool window.
///
/// First-run auto-open is gated by an HKCU settings flag keyed off the
/// extension's <see cref="AssemblyVersionAttribute"/>: bumping the assembly
/// version makes the guide re-open on the next SSMS launch, which is the
/// surface we use to drive attention to documentation updates.
/// </summary>
[PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
[InstalledProductRegistration("#110", "#112", "1.0.0", IconResourceID = 400)]
[ProvideMenuResource("Menus.ctmenu", 1)]
[ProvideToolWindow(typeof(SQuiLGuideToolWindow),
    Style = VsDockStyle.Float, Window = ToolWindowGuids80.SolutionExplorer)]
[ProvideOptionPage(typeof(SQuiL.VisualStudioExtension.Options.SQuiLOptionsPage), "SQuiL", "General", 0, 0, supportsAutomation: true)]
[ProvideAutoLoad(UIContextGuids80.NoSolution, PackageAutoLoadFlags.BackgroundLoad)]
[ProvideAutoLoad(UIContextGuids80.SolutionExists, PackageAutoLoadFlags.BackgroundLoad)]
[Guid(SQuiLPackageGuids.PackageGuidString)]
public sealed class SQuiLPackage : AsyncPackage
{
    protected override async Task InitializeAsync(
        CancellationToken cancellationToken,
        IProgress<ServiceProgressData> progress)
    {
        await base.InitializeAsync(cancellationToken, progress);

        await NewSquilFileCommand.InitializeAsync(this);
        await PreviewGeneratedCSharpCommand.InitializeAsync(this);
        await BuildProjectCommand.InitializeAsync(this);
        await OpenWritingGuideCommand.InitializeAsync(this);
        await CheckForUpdatesCommand.InitializeAsync(this);

        // Background, channel-aware update check. Fire-and-forget AFTER command
        // registration so it never blocks startup — no UI/tool-window work here,
        // just a throttled HTTP call + optional InfoBar, which is safe off the
        // init fence.
        JoinableTaskFactory.RunAsync(() =>
            SQuiL.VisualStudioExtension.Update.SQuiLUpdateChecker.CheckAsync(this, manual: false, DisposalToken))
            .FileAndForget("SQuiL/AutoUpdateCheck");

        // NOTE: first-run guide auto-open is intentionally disabled.
        // Showing a WebView2-hosting tool window from InitializeAsync deadlocked
        // SSMS 22.6 startup during smoke testing — the package load-completion
        // fence and the shell's tool-window creation pipeline both want the
        // UI thread and neither releases it.  The user opens the guide manually
        // from View → Other Windows → SQuiL Writing Guide.  We can re-enable
        // first-run auto-open later by hooking into a shell-idle event
        // (IVsShell::AdviseShellPropertyChanges on VSSPROPID_Zombie=false)
        // rather than running from InitializeAsync.
    }
}
