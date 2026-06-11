using System;

namespace SQuiL.VisualStudioExtension;

/// <summary>
/// Central GUID + ID table.  These values are mirrored in
/// <c>VSPackage\SQuiLPackage.vsct</c>; when you change one, change the other.
/// </summary>
internal static class SQuiLPackageGuids
{
    // ── Package ──────────────────────────────────────────────────────────
    // Distinct from the SSMS extension's GUIDs so both can be installed on the
    // same machine (different products) without colliding.
    public const string PackageGuidString = "b8e2f140-3a5c-4d7e-9f1a-2c4e6b8d0a01";
    public static readonly Guid PackageGuid = new(PackageGuidString);

    // ── Command set (Tools menu + File→New + .squil editor context menu) ─
    public const string CmdSetGuidString = "b8e2f140-3a5c-4d7e-9f1a-2c4e6b8d0a02";
    public static readonly Guid CmdSetGuid = new(CmdSetGuidString);

    public const int CmdIdPreviewGeneratedCSharp = 0x0100;
    public const int CmdIdBuildProject           = 0x0101;
    public const int CmdIdOpenWritingGuide       = 0x0102;
    public const int CmdIdNewFile                = 0x0103;
    public const int CmdIdCheckForUpdates        = 0x0104;

    // ── Tool window (Writing Guide) ────────────────────────────────────
    public const string GuideToolWindowGuidString = "b8e2f140-3a5c-4d7e-9f1a-2c4e6b8d0a03";

    // ── SQuiL editor content type ───────────────────────────────────────
    public const string SQuiLContentTypeName = "squil";
    public const string SQuiLLanguageName    = "SQuiL";
    public const string SQuiLFileExtension   = ".squil";
}
