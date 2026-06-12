using System;
using System.Collections.Generic;

namespace SQuiL.VisualStudioExtension.Update;

/// <summary>
/// Pure release-tag parsing and channel-aware comparison for the update
/// checker. NO VS SDK dependencies, so it is unit-tested in isolation (the
/// SSMS copy is linked into SQuiL.Tests). Mirrors versionInfo.ts in the VS Code
/// extension and the identical copy in the SSMS extension — change
/// one, change the others (see CLAUDE.md port table).
/// </summary>
internal static class SQuiLVersion
{
    public sealed class ParsedTag
    {
        public int[] Sdk { get; set; } = Array.Empty<int>();
        public int Build { get; set; }
        public bool Prerelease { get; set; }
        /// <summary>Dot-separated prerelease identifiers, e.g. ["beta","123"] from "1.0.0-beta.123".</summary>
        public string[] Pre { get; set; } = Array.Empty<string>();
    }

    public sealed class ReleaseInfo
    {
        public string Tag { get; set; } = "";
        public bool Prerelease { get; set; }
        public string HtmlUrl { get; set; } = "";
        public bool HasAsset { get; set; }
    }

    public sealed class UpdateResult
    {
        public string Tag { get; set; } = "";
        public string HtmlUrl { get; set; } = "";
    }

    public static bool IsDevTag(string tag)
        => string.IsNullOrEmpty(tag)
           || (tag.StartsWith("__", StringComparison.Ordinal) && tag.EndsWith("__", StringComparison.Ordinal))
           || ParseTag(tag) is null;

    /// <summary>Parse "&lt;sdk&gt;.&lt;build&gt;[-suffix]" into its parts, or null.</summary>
    public static ParsedTag? ParseTag(string tag)
    {
        if (string.IsNullOrWhiteSpace(tag)) return null;
        var t = tag.Trim();
        var dash = t.IndexOf('-');
        var prerelease = dash >= 0;
        var core = prerelease ? t.Substring(0, dash) : t;
        var parts = core.Split('.');
        if (parts.Length < 2) return null;

        var nums = new int[parts.Length];
        for (var i = 0; i < parts.Length; i++)
            if (!int.TryParse(parts[i], out nums[i])) return null;

        var sdk = new int[nums.Length - 1];
        Array.Copy(nums, sdk, sdk.Length);
        var pre = prerelease ? t.Substring(dash + 1).Split('.') : Array.Empty<string>();
        return new ParsedTag { Sdk = sdk, Build = nums[nums.Length - 1], Prerelease = prerelease, Pre = pre };
    }

    /// <summary>
    /// -1 if a&lt;b, 0 if equal, 1 if a&gt;b. SDK segments first, then build;
    /// equal cores fall through to SemVer rule 11: a release outranks its own
    /// prereleases (1.0.0 &gt; 1.0.0-beta.123), and two prereleases compare
    /// identifier-by-identifier (beta.124 &gt; beta.123).
    /// </summary>
    public static int Compare(ParsedTag a, ParsedTag b)
    {
        var len = Math.Max(a.Sdk.Length, b.Sdk.Length);
        for (var i = 0; i < len; i++)
        {
            var x = i < a.Sdk.Length ? a.Sdk[i] : 0;
            var y = i < b.Sdk.Length ? b.Sdk[i] : 0;
            if (x != y) return x < y ? -1 : 1;
        }
        if (a.Build != b.Build) return a.Build < b.Build ? -1 : 1;
        if (a.Prerelease != b.Prerelease) return a.Prerelease ? -1 : 1;
        if (!a.Prerelease) return 0;
        return ComparePrerelease(a.Pre, b.Pre);
    }

    /// <summary>SemVer rule 11: numeric identifiers compare numerically and rank
    /// below alphanumeric ones; a shorter identifier set ranks below a longer
    /// one when all shared identifiers are equal.</summary>
    private static int ComparePrerelease(string[] a, string[] b)
    {
        var len = Math.Min(a.Length, b.Length);
        for (var i = 0; i < len; i++)
        {
            var aNumeric = int.TryParse(a[i], out var aNum);
            var bNumeric = int.TryParse(b[i], out var bNum);
            if (aNumeric && bNumeric)
            {
                if (aNum != bNum) return aNum < bNum ? -1 : 1;
            }
            else if (aNumeric) return -1;
            else if (bNumeric) return 1;
            else
            {
                var c = string.CompareOrdinal(a[i], b[i]);
                if (c != 0) return c < 0 ? -1 : 1;
            }
        }
        if (a.Length != b.Length) return a.Length < b.Length ? -1 : 1;
        return 0;
    }

    /// <summary>
    /// Newest applicable release strictly newer than <paramref name="currentTag"/>,
    /// or null. Stable channel ignores prereleases; prerelease channel considers
    /// both. Releases without the expected asset are skipped.
    /// </summary>
    public static UpdateResult? SelectUpdate(string currentTag, IEnumerable<ReleaseInfo> releases)
    {
        var current = ParseTag(currentTag);
        if (current is null) return null;
        var onPrerelease = current.Prerelease;

        ParsedTag? bestParsed = null;
        ReleaseInfo? best = null;
        foreach (var r in releases)
        {
            if (r is null || !r.HasAsset) continue;
            if (!onPrerelease && r.Prerelease) continue;
            var p = ParseTag(r.Tag);
            if (p is null) continue;
            if (Compare(p, current) <= 0) continue;
            if (bestParsed is null || Compare(p, bestParsed) > 0) { bestParsed = p; best = r; }
        }
        return best is null ? null : new UpdateResult { Tag = best.Tag, HtmlUrl = best.HtmlUrl };
    }
}
