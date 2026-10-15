using WebChangeRadar.Api.Services.Comparison;
using WebChangeRadar.Api.Services.Extraction;
using Xunit;

namespace WebChangeRadar.UnitTests;

public sealed class ChangeDetectorTests
{
    [Fact]
    public void BaselineEquivalentTextAndChange()
    {
        var extractor = new ContentExtractor();
        var original = extractor.Extract("<p>Hello world</p>");
        var baseline = ChangeDetector.Detect(original, null);
        Assert.Equal(ContentChange.Baseline, baseline.Kind);
        Assert.Equal(64, baseline.ContentHash.Length);
        Assert.Equal(ContentChange.Unchanged,
            ChangeDetector.Detect(extractor.Extract("<p>  Hello   world </p>"), baseline.ContentHash).Kind);
        Assert.Equal(ContentChange.Changed,
            ChangeDetector.Detect(extractor.Extract("<p>Goodbye world</p>"), baseline.ContentHash).Kind);
    }
}
