using System;
using System.ComponentModel.Design;
using System.IO;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Editor;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.TextManager.Interop;
using SQuiL.SsmsExtension.Parsing;
using SQuiL.SsmsExtension.Preview;
using Task = System.Threading.Tasks.Task;

namespace SQuiL.SsmsExtension.Commands;

/// <summary>
/// Implements the "Preview Generated C#" context-menu command.  Generates a
/// preview of what <c>SQuiL.SourceGenerator</c> would emit for the current
/// <c>.squil</c> file, writes it to a temp file, and opens it as a read-only
/// C# document.
///
/// SSMS does not expose a virtual-document scheme equivalent to VS Code's
/// <c>TextDocumentContentProvider</c>, so the on-disk temp file is the most
/// reliable path — the C# language service picks it up automatically and the
/// document opens in a new tab next to the source.  Re-invoking the command
/// refreshes the temp file in place.
/// </summary>
internal sealed class PreviewGeneratedCSharpCommand
{
    private readonly AsyncPackage _package;

    private PreviewGeneratedCSharpCommand(AsyncPackage package, OleMenuCommandService commands)
    {
        _package = package;
        var cmdId = new CommandID(SQuiLPackageGuids.CmdSetGuid, SQuiLPackageGuids.CmdIdPreviewGeneratedCSharp);
        var item  = new OleMenuCommand((s, e) =>
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            Execute();
        }, cmdId);
        item.BeforeQueryStatus += (s, e) =>
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            UpdateVisibility((OleMenuCommand)s);
        };
        commands.AddCommand(item);
    }

    public static async Task InitializeAsync(AsyncPackage package)
    {
        await package.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);
        var commands = await package.GetServiceAsync(typeof(IMenuCommandService)) as OleMenuCommandService
            ?? throw new InvalidOperationException("Failed to obtain OleMenuCommandService.");
        _ = new PreviewGeneratedCSharpCommand(package, commands);
    }

    // ── Query status: show only on .squil documents ──────────────────────

    private void UpdateVisibility(OleMenuCommand cmd)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        bool isSquil = TryGetActiveSquilDocument(out _, out _);
        cmd.Visible = isSquil;
        cmd.Enabled = isSquil;
    }

    // ── Execute ──────────────────────────────────────────────────────────

    private void Execute()
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        if (!TryGetActiveSquilDocument(out var fullPath, out var bufferText))
        {
            VsShellUtilities.ShowMessageBox(
                _package,
                "Preview Generated C# is only available on a .squil document.",
                "SQuiL",
                OLEMSGICON.OLEMSGICON_INFO,
                OLEMSGBUTTON.OLEMSGBUTTON_OK,
                OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
            return;
        }

        var parsed = SQuiLParser.Parse(bufferText);
        string queryName = parsed.QueryName ?? Path.GetFileNameWithoutExtension(fullPath);

        // Resolve [SQuiLQuery]/[SQuiLQueryTransaction] context from disk so the
        // preview shows the correct transaction scaffold.
        var ctx = SQuiL.SsmsExtension.Parsing.SQuiLContextResolver.Resolve(fullPath);
        string preview = SQuiLPreviewGenerator.Generate(parsed, queryName, enabled: ctx.Enabled, debugRollback: ctx.DebugRollback);

        string tempPath = Path.Combine(
            Path.GetTempPath(),
            "SQuiL",
            $"{queryName}.preview.g.cs");

        Directory.CreateDirectory(Path.GetDirectoryName(tempPath)!);

        // Clear the read-only attribute we set last time before overwriting.
        if (File.Exists(tempPath))
            File.SetAttributes(tempPath, FileAttributes.Normal);

        File.WriteAllText(tempPath, preview);
        File.SetAttributes(tempPath, FileAttributes.ReadOnly);

        VsShellUtilities.OpenDocument(
            _package,
            tempPath,
            VSConstants.LOGVIEWID_Code,
            out _, out _, out var frame);

        frame?.Show();
    }

    // ── Active-document helpers ──────────────────────────────────────────

    private bool TryGetActiveSquilDocument(out string fullPath, out string bufferText)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        fullPath   = "";
        bufferText = "";

        var dte = _package.GetService<SDTE, EnvDTE.DTE>();
        var doc = dte?.ActiveDocument;
        if (doc == null) return false;

        string path = doc.FullName;
        if (!path.EndsWith(SQuiLPackageGuids.SQuiLFileExtension, StringComparison.OrdinalIgnoreCase))
            return false;

        var textDoc = doc.Object("TextDocument") as EnvDTE.TextDocument;
        if (textDoc == null) return false;

        fullPath   = path;
        bufferText = textDoc.StartPoint.CreateEditPoint().GetText(textDoc.EndPoint);
        return true;
    }
}
