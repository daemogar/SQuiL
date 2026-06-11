using System;

namespace SQuiL.SsmsExtension;

/// <summary>
/// Central GUID + ID table.  These values are mirrored in
/// <c>VSPackage\SQuiLPackage.vsct</c>; when you change one, change the other.
/// </summary>
internal static class SQuiLPackageGuids
{
    // ── Package ──────────────────────────────────────────────────────────
    public const string PackageGuidString = "9c4d8a72-1f2b-4f3a-8b6e-6c1d3a9e4f01";
    public static readonly Guid PackageGuid = new(PackageGuidString);

    // ── Command set (View/Tools menu + .squil context menu) ─────────────
    public const string CmdSetGuidString = "9c4d8a72-1f2b-4f3a-8b6e-6c1d3a9e4f02";
    public static readonly Guid CmdSetGuid = new(CmdSetGuidString);

    public const int CmdIdPreviewGeneratedCSharp = 0x0100;
    public const int CmdIdBuildProject           = 0x0101;
    public const int CmdIdOpenWritingGuide       = 0x0102;
    public const int CmdIdNewFile                = 0x0103;
    public const int CmdIdCheckForUpdates        = 0x0104;

    // ── Tool window (Writing Guide) ────────────────────────────────────
    public const string GuideToolWindowGuidString = "9c4d8a72-1f2b-4f3a-8b6e-6c1d3a9e4f03";

    // ── SQuiL editor content type ───────────────────────────────────────
    public const string SQuiLContentTypeName = "squil";
    public const string SQuiLLanguageName    = "SQuiL";
    public const string SQuiLFileExtension   = ".squil";
}
