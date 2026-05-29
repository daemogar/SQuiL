using System;
using System.IO;
using System.Reflection;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Wpf;

namespace SQuiL.SsmsExtension.Guide;

/// <summary>
/// WPF host for the SQuiL writing guide.  A single <see cref="WebView2"/>
/// fills the tool window and is navigated to the contents of
/// <c>Resources/guide.html</c> (synced from <c>SQuiL.Editor.Shared</c>
/// at build time).
///
/// The guide HTML is self-contained — inline CSS, no external assets — so
/// <see cref="WebView2.NavigateToString"/> works without VirtualHostName
/// folder mapping.  The one outbound link goes to the SQuiL GitHub repo
/// and opens in the user's default browser (default WebView2 behaviour for
/// target="_blank"-style nav).
///
/// User-data folder is pinned to <c>%LOCALAPPDATA%\SQuiL\WebView2</c> so we
/// don't fight SSMS for the default location and so the cache survives
/// extension upgrades cleanly.
/// </summary>
internal sealed class SQuiLGuideToolWindowControl : UserControl
{
    private readonly WebView2 _webView;
    private bool _initStarted;

    public SQuiLGuideToolWindowControl()
    {
        _webView = new WebView2();
        Content  = _webView;

        Loaded += (_, _) =>
        {
            // WPF fires Loaded every time the control is attached to the
            // visual tree — including reattaches when a tool window is
            // re-docked.  EnsureCoreWebView2Async is one-shot per control;
            // calling it a second time with a different environment throws
            // ("WebView2 was already initialized…").  Gate with a flag.
            if (_initStarted) return;
            _initStarted = true;

            // Fire-and-forget the initialisation — WebView2 takes hundreds
            // of milliseconds to spin up on first use and we don't want to
            // block the UI thread.  Exceptions surface into the WebView
            // itself rather than the SSMS shell.
            _ = InitializeWebViewAsync();
        };
    }

    private async System.Threading.Tasks.Task InitializeWebViewAsync()
    {
        try
        {
            string userDataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SQuiL", "WebView2");
            Directory.CreateDirectory(userDataFolder);

            var env = await Microsoft.Web.WebView2.Core.CoreWebView2Environment
                .CreateAsync(browserExecutableFolder: null, userDataFolder);
            await _webView.EnsureCoreWebView2Async(env);

            _webView.NavigateToString(LoadGuideHtml());
        }
        catch (Exception ex)
        {
            // Fall back to a plain-text error in the WebView's pane if
            // WebView2 initialization fails — typically the runtime missing.
            _webView.NavigateToString(BuildErrorHtml(ex));
        }
    }

    /// <summary>
    /// Load <c>guide.html</c> next to the extension assembly.  MSBuild copies
    /// the file there via the <c>SyncSharedEditorAssets</c> + the
    /// <c>CopyToOutputDirectory</c> metadata in the .csproj.
    /// </summary>
    private static string LoadGuideHtml()
    {
        string asmDir = Path.GetDirectoryName(typeof(SQuiLGuideToolWindowControl).Assembly.Location)
                        ?? AppDomain.CurrentDomain.BaseDirectory;
        string guidePath = Path.Combine(asmDir, "Resources", "guide.html");

        if (File.Exists(guidePath))
            return File.ReadAllText(guidePath);

        return BuildErrorHtml(new FileNotFoundException(
            $"guide.html not found alongside the extension. Expected at: {guidePath}"));
    }

    private static string BuildErrorHtml(Exception ex) =>
        "<!doctype html><html><body style='font-family:Segoe UI;padding:24px;color:#c0392b;'>"
      + "<h2>SQuiL Writing Guide failed to load</h2>"
      + $"<p>{System.Net.WebUtility.HtmlEncode(ex.Message)}</p>"
      + "<p>If the WebView2 Runtime is missing, install it from "
      + "<a href='https://developer.microsoft.com/microsoft-edge/webview2/'>microsoft.com/microsoft-edge/webview2</a>.</p>"
      + "</body></html>";
}
