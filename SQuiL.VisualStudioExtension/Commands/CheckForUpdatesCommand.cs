using System;
using System.ComponentModel.Design;
using Microsoft.VisualStudio.Shell;
using SQuiL.VisualStudioExtension.Update;
using Task = System.Threading.Tasks.Task;

namespace SQuiL.VisualStudioExtension.Commands;

/// <summary>
/// Tools › SQuiL › Check for Updates. Always runs (ignores the disable option
/// and the 24h throttle) and reports a result.
/// </summary>
internal sealed class CheckForUpdatesCommand
{
    private readonly AsyncPackage _package;

    private CheckForUpdatesCommand(AsyncPackage package, OleMenuCommandService commands)
    {
        _package = package;
        var cmdId = new CommandID(SQuiLPackageGuids.CmdSetGuid, SQuiLPackageGuids.CmdIdCheckForUpdates);
        commands.AddCommand(new MenuCommand((s, e) =>
        {
            _package.JoinableTaskFactory.RunAsync(() =>
                SQuiLUpdateChecker.CheckAsync(_package, manual: true, _package.DisposalToken)).FileAndForget("SQuiL/CheckForUpdates");
        }, cmdId));
    }

    public static async Task InitializeAsync(AsyncPackage package)
    {
        await package.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);
        var commands = await package.GetServiceAsync(typeof(IMenuCommandService)) as OleMenuCommandService
            ?? throw new InvalidOperationException("Failed to obtain OleMenuCommandService.");
        _ = new CheckForUpdatesCommand(package, commands);
    }
}
