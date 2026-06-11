namespace SQuiL.VisualStudioExtension.Update;

/// <summary>
/// Release identity baked in at publish time by .github/workflows/publish.yml,
/// which replaces the placeholder below with the GitHub release tag. Local/dev
/// builds keep the placeholder; <see cref="SQuiLVersion.IsDevTag"/> treats that
/// as a dev build and the automatic update check is skipped.
/// </summary>
internal static class BuildInfo
{
    public const string ReleaseTag = "__SQUIL_RELEASE_TAG__";
}
