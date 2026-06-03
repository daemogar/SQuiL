using System;
using System.ComponentModel.Design;
using System.IO;
using Microsoft.VisualStudio.Shell;
using Task = System.Threading.Tasks.Task;

namespace SQuiL.VisualStudioExtension.Commands;

/// <summary>
/// "Tools → New SQuiL File" command.  Creates an empty <c>.squil</c> file and opens it.
///
/// SSMS keys every SQuiL feature (classification, completion, F5/Execute, lints)
/// off the <c>.squil</c> file extension via <c>SQuiLContentTypeDefinition.IsSquilBuffer</c>
/// and the SQL Query Editor binding in <c>SQuiL.Bindings.pkgdef</c>.  An untitled
/// in-memory buffer has no extension, so it wouldn't light up.  We therefore write
/// an empty <c>.squil</c> file to a unique temp folder and open it through the bound
/// editor factory; the user can <em>Save As</em> to the real destination afterward.
/// </summary>
internal sealed class NewSquilFileCommand
{
    private readonly AsyncPackage _package;

    private NewSquilFileCommand(AsyncPackage package, OleMenuCommandService commands)
    {
        _package = package;
        var cmdId = new CommandID(SQuiLPackageGuids.CmdSetGuid, SQuiLPackageGuids.CmdIdNewFile);
        var item  = new MenuCommand((s, e) =>
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            CreateNewFile();
        }, cmdId);
        commands.AddCommand(item);
    }

    public static async Task InitializeAsync(AsyncPackage package)
    {
        await package.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);
        var commands = await package.GetServiceAsync(typeof(IMenuCommandService)) as OleMenuCommandService
            ?? throw new InvalidOperationException("Failed to obtain OleMenuCommandService.");
        _ = new NewSquilFileCommand(package, commands);
    }

    private void CreateNewFile()
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        var dir = Path.Combine(Path.GetTempPath(), "SQuiL", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);

        var path = Path.Combine(dir, "Query1.squil");
        File.WriteAllText(path, string.Empty);

        // Opens with the default editor registered for .squil — the SQL Query
        // Editor factory (see SQuiL.Bindings.pkgdef), so the new file gets full
        // SQuiL features and F5/Execute immediately.
        VsShellUtilities.OpenDocument(
            _package, path, Guid.Empty, out _, out _, out _);
    }
}
