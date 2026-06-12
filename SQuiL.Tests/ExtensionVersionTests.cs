using SQuiL.SsmsExtension.Update;
using Xunit;

namespace SQuiL.Tests;

public class ExtensionVersionTests
{
    [Fact]
    public void ParseTag_reads_sdk_build_and_prerelease()
    {
        var p = SQuiLVersion.ParseTag("10.0.100.0042-beta");
        Assert.NotNull(p);
        Assert.Equal(new[] { 10, 0, 100 }, p!.Sdk);
        Assert.Equal(42, p.Build);
        Assert.True(p.Prerelease);
    }

    [Fact]
    public void ParseTag_treats_no_suffix_as_stable()
    {
        var p = SQuiLVersion.ParseTag("10.0.100.0050");
        Assert.NotNull(p);
        Assert.False(p!.Prerelease);
        Assert.Equal(50, p.Build);
    }

    [Theory]
    [InlineData("not-a-tag")]
    [InlineData("")]
    public void ParseTag_rejects_garbage(string tag)
        => Assert.Null(SQuiLVersion.ParseTag(tag));

    [Fact]
    public void Compare_orders_by_sdk_then_build()
    {
        Assert.Equal(-1, SQuiLVersion.Compare(SQuiLVersion.ParseTag("10.0.100.0042")!, SQuiLVersion.ParseTag("10.0.100.0050")!));
        Assert.Equal(1, SQuiLVersion.Compare(SQuiLVersion.ParseTag("10.0.200.0001")!, SQuiLVersion.ParseTag("10.0.100.9999")!));
        Assert.Equal(0, SQuiLVersion.Compare(SQuiLVersion.ParseTag("10.0.100.0042")!, SQuiLVersion.ParseTag("10.0.100.0042")!));
    }

    private static SQuiLVersion.ReleaseInfo[] Sample() => new[]
    {
        new SQuiLVersion.ReleaseInfo { Tag = "10.0.100.0060-beta", Prerelease = true,  HtmlUrl = "u60", HasAsset = true },
        new SQuiLVersion.ReleaseInfo { Tag = "10.0.100.0055",      Prerelease = false, HtmlUrl = "u55", HasAsset = true },
        new SQuiLVersion.ReleaseInfo { Tag = "10.0.100.0050-beta", Prerelease = true,  HtmlUrl = "u50", HasAsset = true },
        new SQuiLVersion.ReleaseInfo { Tag = "10.0.100.0070-beta", Prerelease = true,  HtmlUrl = "u70", HasAsset = false },
    };

    [Fact]
    public void SelectUpdate_stable_ignores_betas_and_picks_newest_stable()
    {
        var r = SQuiLVersion.SelectUpdate("10.0.100.0040", Sample());
        Assert.Equal("10.0.100.0055", r!.Tag);
    }

    [Fact]
    public void SelectUpdate_stable_returns_null_when_already_newest_stable()
        => Assert.Null(SQuiLVersion.SelectUpdate("10.0.100.0055", Sample()));

    [Fact]
    public void SelectUpdate_prerelease_sees_both_and_skips_assetless()
    {
        var r = SQuiLVersion.SelectUpdate("10.0.100.0052-beta", Sample());
        Assert.Equal("10.0.100.0060-beta", r!.Tag);
    }

    [Theory]
    [InlineData("__SQUIL_RELEASE_TAG__", true)]
    [InlineData("garbage", true)]
    [InlineData("10.0.100.0042-beta", false)]
    [InlineData("1.0.0-beta.123", false)]
    [InlineData("1.0.0", false)]
    public void IsDevTag_flags_placeholder_and_unparseable(string tag, bool expected)
        => Assert.Equal(expected, SQuiLVersion.IsDevTag(tag));

    // ── SemVer scheme: <next>-beta.<run#> betas, plain MAJOR.MINOR.PATCH officials ──

    [Fact]
    public void ParseTag_reads_semver_beta()
    {
        var p = SQuiLVersion.ParseTag("1.0.0-beta.123");
        Assert.NotNull(p);
        Assert.True(p!.Prerelease);
    }

    [Fact]
    public void Compare_official_is_newer_than_its_own_beta()
    {
        Assert.Equal(1, SQuiLVersion.Compare(SQuiLVersion.ParseTag("1.0.0")!, SQuiLVersion.ParseTag("1.0.0-beta.123")!));
        Assert.Equal(-1, SQuiLVersion.Compare(SQuiLVersion.ParseTag("1.0.0-beta.123")!, SQuiLVersion.ParseTag("1.0.0")!));
    }

    [Fact]
    public void Compare_orders_betas_by_run_number()
    {
        Assert.Equal(1, SQuiLVersion.Compare(SQuiLVersion.ParseTag("1.0.0-beta.124")!, SQuiLVersion.ParseTag("1.0.0-beta.123")!));
        Assert.Equal(-1, SQuiLVersion.Compare(SQuiLVersion.ParseTag("1.0.0-beta.9")!, SQuiLVersion.ParseTag("1.0.0-beta.10")!));
        Assert.Equal(0, SQuiLVersion.Compare(SQuiLVersion.ParseTag("1.0.0-beta.123")!, SQuiLVersion.ParseTag("1.0.0-beta.123")!));
    }

    [Fact]
    public void Compare_next_cycle_beta_is_newer_than_prior_official()
        => Assert.Equal(1, SQuiLVersion.Compare(SQuiLVersion.ParseTag("1.0.1-beta.1")!, SQuiLVersion.ParseTag("1.0.0")!));

    private static SQuiLVersion.ReleaseInfo[] SemVerSample() => new[]
    {
        new SQuiLVersion.ReleaseInfo { Tag = "1.0.0-beta.120", Prerelease = true,  HtmlUrl = "b120", HasAsset = true },
        new SQuiLVersion.ReleaseInfo { Tag = "1.0.0-beta.125", Prerelease = true,  HtmlUrl = "b125", HasAsset = true },
        new SQuiLVersion.ReleaseInfo { Tag = "1.0.0",          Prerelease = false, HtmlUrl = "v100", HasAsset = true },
    };

    [Fact]
    public void SelectUpdate_beta_user_is_offered_the_official_release()
    {
        var r = SQuiLVersion.SelectUpdate("1.0.0-beta.125", SemVerSample());
        Assert.Equal("1.0.0", r!.Tag);
    }

    [Fact]
    public void SelectUpdate_beta_user_is_offered_newer_beta_when_no_official_yet()
    {
        var releases = new[]
        {
            new SQuiLVersion.ReleaseInfo { Tag = "1.0.0-beta.120", Prerelease = true, HtmlUrl = "b120", HasAsset = true },
            new SQuiLVersion.ReleaseInfo { Tag = "1.0.0-beta.125", Prerelease = true, HtmlUrl = "b125", HasAsset = true },
        };
        var r = SQuiLVersion.SelectUpdate("1.0.0-beta.120", releases);
        Assert.Equal("1.0.0-beta.125", r!.Tag);
    }

    [Fact]
    public void SelectUpdate_stable_user_on_official_is_not_offered_anything()
        => Assert.Null(SQuiLVersion.SelectUpdate("1.0.0", SemVerSample()));
}
