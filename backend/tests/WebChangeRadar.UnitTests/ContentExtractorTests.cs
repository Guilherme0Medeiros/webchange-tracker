using WebChangeRadar.Api.Services.Extraction;
using Xunit;

namespace WebChangeRadar.UnitTests;

public sealed class ContentExtractorTests
{
    private readonly ContentExtractor extractor = new();

    [Fact]
    public void SelectedContentIgnoresChangesOutsideSelectionAndReportsMissingElement()
    {
        Assert.Equal("Current", extractor.ExtractSelected("<main><p>Current</p></main><aside>Old</aside>", "main"));
        Assert.Equal("Current", extractor.ExtractSelected("<main><p>Current</p></main><aside>New</aside>", "main"));
        Assert.Null(extractor.ExtractSelected("<main>Current</main>", "#missing"));
        Assert.False(ContentExtractor.IsValidSelector("["));
    }

    [Fact]
    public void ComplexSelectorsAndMultipleMatchesKeepSelectedValuesSeparate()
    {
        const string html = "<main><div class='content'><span class='item'>Alpha</span>" +
            "<span class='item'>Beta</span></div></main><p id='status'>Ready</p>";
        Assert.Equal("Alpha\nBeta", extractor.ExtractSelected(html, "main .content .item"));
        Assert.Equal("Ready", extractor.ExtractSelected(html, "#status"));
        Assert.Equal("AlphaBeta", extractor.ExtractSelected(html, "main .content"));
    }

    [Fact]
    public void HiddenOrEmptySelectionDoesNotReplaceExistingContentWithEmptySnapshot()
    {
        Assert.Null(extractor.ExtractSelected("<main hidden><h1>Hidden</h1></main>", "h1"));
        Assert.Null(extractor.ExtractSelected("<main><h1></h1></main>", "h1"));
    }

    [Fact]
    public void EquivalentSelectedTextNormalizesWhitespaceAndUnicode()
    {
        Assert.Equal(extractor.ExtractSelected("<h1>Caf\u00e9 alert</h1>", "h1"),
            extractor.ExtractSelected("<h1> Cafe\u0301   alert </h1>", "h1"));
    }

    [Fact]
    public void ExtractsVisibleTextWithBlockAndInlineSeparation()
    {
        var html = "<h1>News</h1><p>Hello <strong>world</strong>!</p><p>Next<br>line</p>";
        Assert.Equal("News\nHello world!\nNext\nline", extractor.Extract(html));
    }

    [Fact]
    public void ExcludesScriptsStylesNoscriptAndHiddenContent()
    {
        var html = "<head><title>Page title</title><style>bad</style></head>" +
            "<body><script>bad</script><noscript>bad</noscript><template>bad</template>" +
            "<p hidden>bad</p><p aria-hidden='true'>bad</p>" +
            "<p style='display: none'>bad</p><p style='visibility:hidden'>bad</p>" +
            "<p>Visible</p></body>";
        Assert.Equal("Visible", extractor.Extract(html));
        Assert.Equal("<p>Visible</p>", extractor.Extract(html, ContentMode.Html));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \r\n\t ")]
    public void EmptyInputProducesEmptyOutput(string? html)
    {
        Assert.Equal(string.Empty, extractor.Extract(html));
        Assert.Equal(string.Empty, extractor.Extract(html, ContentMode.Html));
    }

    [Fact]
    public void RecoversIncompleteHtml()
    {
        Assert.Equal("First\nSecond", extractor.Extract("<p>First<p>Second"));
        Assert.Equal("<p>First</p><p>Second</p>",
            extractor.Extract("<p>First<p>Second", ContentMode.Html));
    }

    [Fact]
    public void NormalizesWhitespaceEntitiesAndUnicode()
    {
        var first = "<p>Caf\u00e9&nbsp; today</p>";
        var second = "<p> Cafe\u0301\r\n\t today </p>";
        Assert.Equal("Café today", extractor.Extract(first));
        Assert.Equal(extractor.Extract(first), extractor.Extract(second));
        Assert.Equal(extractor.Extract(first, ContentMode.Html),
            extractor.Extract(second, ContentMode.Html));
    }

    [Fact]
    public void CollapsesWhitespaceAcrossInlineElements()
    {
        Assert.Equal("A B", extractor.Extract("<p>A <span> B</span></p>"));
        Assert.Equal(extractor.Extract("<p>A <span>B</span></p>"),
            extractor.Extract("<p>A <span> B</span></p>"));
    }

    [Fact]
    public void HtmlModeSortsMeaningfulAttributesAndDropsPresentationNoise()
    {
        var first = "<a class='old' href='/release' title='Latest' data-id='1'>Read</a>";
        var second = "<a data-id='2' title='Latest' href='/release' class='new'>Read</a>";
        var expected = "<a href=\"/release\" title=\"Latest\">Read</a>";
        Assert.Equal(expected, extractor.Extract(first, ContentMode.Html));
        Assert.Equal(expected, extractor.Extract(second, ContentMode.Html));
    }

    [Fact]
    public void IgnoresFormattingOnlyWhitespaceBetweenBlocks()
    {
        var compact = "<main><p>One</p><p>Two</p></main>";
        var formatted = "<main>\n  <p>One</p>\n  <p>Two</p>\n</main>";
        Assert.Equal(extractor.Extract(compact), extractor.Extract(formatted));
        Assert.Equal(extractor.Extract(compact, ContentMode.Html),
            extractor.Extract(formatted, ContentMode.Html));
    }

    [Fact]
    public void PreservesActualTextAndLinkChanges()
    {
        Assert.NotEqual(extractor.Extract("<p>Open</p>"), extractor.Extract("<p>Closed</p>"));
        Assert.NotEqual(extractor.Extract("<a href='/a'>Read</a>", ContentMode.Html),
            extractor.Extract("<a href='/b'>Read</a>", ContentMode.Html));
    }
}
