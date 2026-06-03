using System;
using System.ComponentModel.Design;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using SQuiL.VisualStudioExtension.Guide;
using Task = System.Threading.Tasks.Task;

namespace SQuiL.VisualStudioExtension.Commands;

/// <summary>
/// "View → Other Windows → SQuiL Writing Guide" command.  Shows the
/// <see cref="SQuiLGuideToolWindow"/>, creating it if it doesn't exist yet.
///
/// Reused by <see cref="SQuiLPackage.InitializeAsync"/> for the first-run
/// auto-open, so the show logic lives here in one place.
/// </summary>
internal sealed class OpenWritingGuideCommand
{
    private readonly AsyncPackage _package;

    private OpenWritingGuideCommand(AsyncPackage package, OleMenuCommandService commands)
    {
        _package = package;
        var cmdId = new CommandID(SQuiLPackageGuids.CmdSetGuid, SQuiLPackageGuids.CmdIdOpenWritingGuide);
        var item  = new MenuCommand((s, e) =>
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            ShowToolWindow();
        }, cmdId);
        commands.AddCommand(item);
    }

    public static async Task InitializeAsync(AsyncPackage package)
    {
        await package.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);
        var commands = await package.GetServiceAsync(typeof(IMenuCommandService)) as OleMenuCommandService
            ?? throw new InvalidOperationException("Failed to obtain OleMenuCommandService.");
        _ = new OpenWritingGuideCommand(package, commands);
    }

    /// <summary>
    /// Find (or create) the SQuiL Writing Guide tool window and bring it to
    /// the front.  Exposed as <c>internal static</c> so the package's
    /// first-run auto-open can call it directly.
    /// </summary>
    internal static void ShowToolWindow(AsyncPackage package)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        ShowToolWindow(package, throwOnFailure: true);
    }

    private void ShowToolWindow()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        ShowToolWindow(_package, throwOnFailure: false);
    }

    private static void ShowToolWindow(AsyncPackage package, bool throwOnFailure)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        // FindToolWindow with create:true instantiates a pane if none exists.
        var window = package.FindToolWindow(typeof(SQuiLGuideToolWindow), 0, create: true);
        if (window?.Frame is not IVsWindowFrame frame)
        {
            if (throwOnFailure)
                throw new NotSupportedException("Cannot create the SQuiL Writing Guide tool window.");
            return;
        }

        ErrorHandler.ThrowOnFailure(frame.Show());
    }
}
