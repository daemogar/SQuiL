using SQuiL.Tools.GuideRenderer;

using Xunit;

namespace SQuiL.Tests;

public class GuideTemplateTests
{
	[Fact]
	public void UnmarkedContentPassesThrough()
		=> Assert.Equal("line one\nline two\n",
			GuideTemplate.Render("line one\nline two\n", "vscode"));

	[Fact]
	public void VscodeBlockKeptForVscodeDroppedForOthers()
	{
		var t = "shared\n<!--#if vscode-->\nonly-vscode\n<!--#endif-->\ntail\n";
		Assert.Equal("shared\nonly-vscode\ntail\n", GuideTemplate.Render(t, "vscode"));
		Assert.Equal("shared\ntail\n", GuideTemplate.Render(t, "ssms"));
	}

	[Fact]
	public void MultiTokenBlockKeptForListedEnvironments()
	{
		var t = "<!--#if ssms visualstudio-->\nshared-ide\n<!--#endif-->\n";
		Assert.Equal("shared-ide\n", GuideTemplate.Render(t, "ssms"));
		Assert.Equal("shared-ide\n", GuideTemplate.Render(t, "visualstudio"));
		Assert.Equal("", GuideTemplate.Render(t, "vscode"));
	}

	[Fact]
	public void MarkerLinesAreRemoved()
	{
		var result = GuideTemplate.Render("<!--#if vscode-->\nx\n<!--#endif-->\n", "vscode");
		Assert.DoesNotContain("#if", result);
		Assert.DoesNotContain("#endif", result);
	}

	[Theory]
	[InlineData("<!--#if vscode-->\nx\n")]                  // unbalanced (no endif)
	[InlineData("x\n<!--#endif-->\n")]                      // stray endif
	[InlineData("<!--#if vscode-->\n<!--#if ssms-->\ny\n")] // nested
	[InlineData("<!--#if msvc-->\nx\n<!--#endif-->\n")]     // unknown token
	public void MalformedTemplatesThrow(string template)
		=> Assert.Throws<GuideTemplateException>(() => GuideTemplate.Render(template, "vscode"));

	[Fact]
	public void UnknownEnvironmentArgumentThrows()
		=> Assert.Throws<GuideTemplateException>(() => GuideTemplate.Render("x", "eclipse"));

	[Fact]
	public void CarriageReturnsOnContentLinesArePreservedSeparatorsJoinWithLf()
	{
		// CRLF template: marker lines (matched via Trim) drop regardless of \r;
		// kept content lines retain their trailing \r; the renderer joins with \n.
		var t = "shared\r\n<!--#if vscode-->\r\nonly-vscode\r\n<!--#endif-->\r\ntail\r\n";
		Assert.Equal("shared\r\nonly-vscode\r\ntail\r\n", GuideTemplate.Render(t, "vscode"));
		Assert.Equal("shared\r\ntail\r\n", GuideTemplate.Render(t, "ssms"));
	}
}
