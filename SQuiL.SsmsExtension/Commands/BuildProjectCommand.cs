using System;
using System.ComponentModel.Design;
using System.Diagnostics;
using System.IO;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Task = System.Threading.Tasks.Task;

namespace SQuiL.SsmsExtension.Commands;

/// <summary>
/// "Build SQuiL Project" command.  Walks up from the active <c>.squil</c>
/// file looking for the nearest <c>.csproj</c> (or <c>.sln</c>) and shells
/// out to <c>dotnet build</c>.  Output is streamed to a dedicated
/// "SQuiL Build" pane in the Output window so the user can see the
/// source-generator step run end-to-end without leaving SSMS.
///
/// Build is intentionally fire-and-forget from a UI standpoint — the command
/// returns immediately after launching <c>dotnet</c> and the user reads the
/// streaming pane.  No project tree integration is attempted; the dotnet CLI
/// is the source of truth for what SQuiL emits.
/// </summary>
internal sealed class BuildProjectCommand
{
    private static readonly Guid OutputPaneGuid = new("a2b8c4d6-1e3f-4a5b-8c7d-9f0e1d2c3b4a");
    private const string OutputPaneName = "SQuiL Build";

    private readonly AsyncPackage _package;

    private BuildProjectCommand(AsyncPackage package, OleMenuCommandService commands)
    {
        _package = package;
        var cmdId = new CommandID(SQuiLPackageGuids.CmdSetGuid, SQuiLPackageGuids.CmdIdBuildProject);
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
        _ = new BuildProjectCommand(package, commands);
    }

    // ── Visibility ──────────────────────────────────────────────────────

    private void UpdateVisibility(OleMenuCommand cmd)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        bool ok = TryGetActiveSquilPath(out _);
        cmd.Visible = ok;
        cmd.Enabled = ok;
    }

    // ── Execute ─────────────────────────────────────────────────────────

    private void Execute()
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        if (!TryGetActiveSquilPath(out string fullPath))
        {
            ShowInfo("Build SQuiL Project is only available on a .squil document.");
            return;
        }

        string? buildTarget = FindNearestBuildTarget(Path.GetDirectoryName(fullPath));
        if (buildTarget == null)
        {
            ShowInfo("No .csproj or .sln found in any parent directory of the active .squil file.");
            return;
        }

        var pane = EnsureOutputPane();
        pane?.Activate();
        WritePane(pane, $"┌─ SQuiL build ─────────────────────────────────────────┐\r\n");
        WritePane(pane, $"│ Target: {buildTarget}\r\n");
        WritePane(pane, $"│ Working dir: {Path.GetDirectoryName(buildTarget)}\r\n");
        WritePane(pane, $"└───────────────────────────────────────────────────────┘\r\n");

        StartBuild(buildTarget, pane);
    }

    private void StartBuild(string buildTarget, IVsOutputWindowPane? pane)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName               = "dotnet",
                Arguments              = $"build \"{buildTarget}\" -nologo",
                WorkingDirectory       = Path.GetDirectoryName(buildTarget),
                UseShellExecute        = false,
                CreateNoWindow         = true,
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
            };

            var p = new Process { StartInfo = psi, EnableRaisingEvents = true };

            // Output streams arrive on threadpool threads; marshal onto the
            // UI thread to satisfy IVsOutputWindowPane's threading contract.
            p.OutputDataReceived += (_, e) => { if (e.Data != null) WritePaneFromAnyThread(pane, e.Data + "\r\n"); };
            p.ErrorDataReceived  += (_, e) => { if (e.Data != null) WritePaneFromAnyThread(pane, e.Data + "\r\n"); };
            p.Exited             += (_, _) => WritePaneFromAnyThread(pane,
                $"\r\n── Exit code: {p.ExitCode} ────────────────────────────\r\n");

            p.Start();
            p.BeginOutputReadLine();
            p.BeginErrorReadLine();
        }
        catch (Exception ex)
        {
            WritePane(pane, $"Failed to launch dotnet build: {ex.Message}\r\n");
        }
    }

    // ── Helpers ─────────────────────────────────────────────────────────

    /// <summary>
    /// Walk up from <paramref name="startDir"/> until we find a <c>.csproj</c>
    /// or <c>.sln</c>.  Prefer <c>.csproj</c> when both are present in the
    /// same folder — building the project alone is faster than building the
    /// whole solution and gives the same source-generator output.
    /// </summary>
    private static string? FindNearestBuildTarget(string? startDir)
    {
        var dir = startDir == null ? null : new DirectoryInfo(startDir);
        while (dir != null)
        {
            var csproj = dir.GetFiles("*.csproj");
            if (csproj.Length > 0) return csproj[0].FullName;

            var sln = dir.GetFiles("*.sln");
            if (sln.Length > 0) return sln[0].FullName;

            dir = dir.Parent;
        }
        return null;
    }

    private bool TryGetActiveSquilPath(out string fullPath)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        fullPath = "";

        var dte = _package.GetService<SDTE, EnvDTE.DTE>();
        var doc = dte?.ActiveDocument;
        if (doc == null) return false;

        if (!doc.FullName.EndsWith(SQuiLPackageGuids.SQuiLFileExtension, StringComparison.OrdinalIgnoreCase))
            return false;

        fullPath = doc.FullName;
        return true;
    }

    private IVsOutputWindowPane? EnsureOutputPane()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var window = _package.GetService<SVsOutputWindow, IVsOutputWindow>();
        if (window == null) return null;

        var guid = OutputPaneGuid;
        window.CreatePane(ref guid, OutputPaneName, fInitVisible: 1, fClearWithSolution: 0);
        window.GetPane(ref guid, out var pane);
        return pane;
    }

    private void WritePane(IVsOutputWindowPane? pane, string text)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (pane == null || string.IsNullOrEmpty(text)) return;
        pane.OutputStringThreadSafe(text);
    }

#pragma warning disable VSTHRD010 // OutputStringThreadSafe is, as its name says, thread-safe.
    private void WritePaneFromAnyThread(IVsOutputWindowPane? pane, string text)
    {
        if (pane == null || string.IsNullOrEmpty(text)) return;
        pane.OutputStringThreadSafe(text);
    }
#pragma warning restore VSTHRD010

    private void ShowInfo(string message)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        VsShellUtilities.ShowMessageBox(
            _package,
            message,
            "SQuiL",
            OLEMSGICON.OLEMSGICON_INFO,
            OLEMSGBUTTON.OLEMSGBUTTON_OK,
            OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
    }
}
