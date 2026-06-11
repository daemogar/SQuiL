using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Imaging;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.Win32;
using Newtonsoft.Json.Linq;
using SQuiL.SsmsExtension.Options;

namespace SQuiL.SsmsExtension.Update;

/// <summary>
/// GitHub release check for the SSMS extension. Mirrors updateChecker.ts in the
/// VS Code extension and the identical copy in the Visual Studio extension.
/// Manual checks ignore the disable option and the 24h throttle and always
/// report a result; automatic checks fail silently.
/// </summary>
internal static class SQuiLUpdateChecker
{
    private const string ReleasesUrl = "https://api.github.com/repos/daemogar/SQuiL/releases";
    private const string AssetName = "SQuiL.SsmsExtension.vsix";
    private const string SettingsKey = @"Software\SQuiL\SsmsExtension";
    private const string LastCheckValue = "LastUpdateCheckUtcTicks";
    private static readonly TimeSpan Throttle = TimeSpan.FromHours(24);

    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("SQuiL-SsmsExtension");
        c.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return c;
    }

    public static async Task CheckAsync(AsyncPackage package, bool manual, CancellationToken ct)
    {
        var currentTag = BuildInfo.ReleaseTag;

        if (!manual)
        {
            // GetEnableOption reads a DialogPage — must be on the UI thread.
            await package.JoinableTaskFactory.SwitchToMainThreadAsync(ct);
            if (!GetEnableOption(package)) return;
        }

        if (SQuiLVersion.IsDevTag(currentTag))
        {
            if (manual)
                await ShowMessageAsync(package, "This is a local/dev build — update checks compare against released builds only.");
            return;
        }

        var onPrerelease = SQuiLVersion.ParseTag(currentTag)?.Prerelease ?? false;
        if (!manual && onPrerelease && WithinThrottle()) return;

        List<SQuiLVersion.ReleaseInfo> releases;
        try
        {
            var json = await Http.GetStringAsync(ReleasesUrl).ConfigureAwait(false);
            releases = ParseReleases(json);
        }
        catch (Exception ex)
        {
            if (manual)
                await ShowMessageAsync(package, "Couldn't reach GitHub to check for updates. " + ex.Message);
            return;
        }
        finally
        {
            if (!manual && onPrerelease) StampLastCheck();
        }

        var update = SQuiLVersion.SelectUpdate(currentTag, releases);
        if (update is null)
        {
            if (manual)
                await ShowMessageAsync(package, "You are running the latest version.");
            return;
        }

        await ShowUpdateInfoBarAsync(package, update.Tag, update.HtmlUrl);
    }

    private static List<SQuiLVersion.ReleaseInfo> ParseReleases(string json)
    {
        var list = new List<SQuiLVersion.ReleaseInfo>();
        foreach (var r in JArray.Parse(json))
        {
            var assets = r["assets"] as JArray;
            var hasAsset = assets != null
                && assets.Any(a => string.Equals((string?)a["name"], AssetName, StringComparison.OrdinalIgnoreCase));
            list.Add(new SQuiLVersion.ReleaseInfo
            {
                Tag = (string?)r["tag_name"] ?? "",
                Prerelease = (bool?)r["prerelease"] ?? false,
                HtmlUrl = (string?)r["html_url"] ?? "",
                HasAsset = hasAsset,
            });
        }
        return list;
    }

    private static bool WithinThrottle()
    {
        using var key = Registry.CurrentUser.OpenSubKey(SettingsKey);
        if (key?.GetValue(LastCheckValue) is not string raw) return false;
        if (!long.TryParse(raw, out var ticks)) return false;
        return DateTime.UtcNow - new DateTime(ticks, DateTimeKind.Utc) < Throttle;
    }

    private static void StampLastCheck()
    {
        using var key = Registry.CurrentUser.CreateSubKey(SettingsKey);
        key?.SetValue(LastCheckValue, DateTime.UtcNow.Ticks.ToString());
    }

    private static bool GetEnableOption(AsyncPackage package)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var page = (SQuiLOptionsPage)package.GetDialogPage(typeof(SQuiLOptionsPage));
        return page.EnableUpdateCheck;
    }

    private static async Task ShowMessageAsync(AsyncPackage package, string message)
    {
        await package.JoinableTaskFactory.SwitchToMainThreadAsync();
        VsShellUtilities.ShowMessageBox(
            package, message, "SQuiL",
            OLEMSGICON.OLEMSGICON_INFO, OLEMSGBUTTON.OLEMSGBUTTON_OK,
            OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
    }

    private static async Task ShowUpdateInfoBarAsync(AsyncPackage package, string tag, string htmlUrl)
    {
        await package.JoinableTaskFactory.SwitchToMainThreadAsync();

        var shell = await package.GetServiceAsync(typeof(SVsShell)) as IVsShell;
        var factory = await package.GetServiceAsync(typeof(SVsInfoBarUIFactory)) as IVsInfoBarUIFactory;
        if (shell is null || factory is null)
        {
            VsShellUtilities.ShowMessageBox(
                package, $"SQuiL {tag} is available: {htmlUrl}", "SQuiL",
                OLEMSGICON.OLEMSGICON_INFO, OLEMSGBUTTON.OLEMSGBUTTON_OK,
                OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
            return;
        }

        if (ErrorHandler.Failed(shell.GetProperty((int)__VSSPROPID7.VSSPROPID_MainWindowInfoBarHost, out var hostObj)))
            return;
        if (hostObj is not IVsInfoBarHost host)
            return;

        var model = new InfoBarModel(
            new[] { new InfoBarTextSpan($"SQuiL {tag} is available.") },
            new[] { new InfoBarHyperlink("View Release", htmlUrl) },
            KnownMonikers.StatusInformation,
            isCloseButtonVisible: true);

        var element = factory.CreateInfoBar(model);
        _ = new UpdateInfoBarEvents(element, host);
        host.AddInfoBar(element);
    }

    /// <summary>Wires the InfoBar hyperlink + close button and self-unsubscribes.</summary>
    private sealed class UpdateInfoBarEvents : IVsInfoBarUIEvents
    {
        private readonly IVsInfoBarUIElement _element;
        private readonly IVsInfoBarHost _host;
        private uint _cookie;

        public UpdateInfoBarEvents(IVsInfoBarUIElement element, IVsInfoBarHost host)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            _element = element;
            _host = host;
            element.Advise(this, out _cookie);
        }

        public void OnClosed(IVsInfoBarUIElement infoBarUIElement)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            _element.Unadvise(_cookie);
        }

        public void OnActionItemClicked(IVsInfoBarUIElement infoBarUIElement, IVsInfoBarActionItem actionItem)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (actionItem.ActionContext is string url && !string.IsNullOrEmpty(url))
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            _host.RemoveInfoBar(infoBarUIElement);
        }
    }
}
