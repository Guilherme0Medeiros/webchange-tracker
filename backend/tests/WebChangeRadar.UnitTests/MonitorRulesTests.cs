using WebChangeRadar.Api.Features.Monitors;
using Xunit;

namespace WebChangeRadar.UnitTests;

public sealed class MonitorRulesTests
{
    [Fact]
    public void KeywordsFilterAlertsWithoutChangingTheText()
    {
        var json = WebChangeRadar.Api.Features.Monitors.MonitorRules.SerializeKeywords([" Café ", "café"]);
        Assert.Single(WebChangeRadar.Api.Features.Monitors.MonitorRules.ReadKeywords(json));
        Assert.True(WebChangeRadar.Api.Features.Monitors.MonitorRules.ShouldNotify("Novo CAFÉ hoje", json));
        Assert.False(WebChangeRadar.Api.Features.Monitors.MonitorRules.ShouldNotify("Sem correspondência", json));
        Assert.True(WebChangeRadar.Api.Features.Monitors.MonitorRules.ShouldNotify("Qualquer mudança", "[]"));
    }
    [Theory]
    [InlineData(60)]
    [InlineData(360)]
    [InlineData(1440)]
    public void AcceptsSupportedIntervals(int interval) =>
        Assert.Empty(MonitorRules.Validate("News", "https://example.org/page", interval));

    [Theory]
    [InlineData(0)]
    [InlineData(30)]
    [InlineData(61)]
    public void RejectsUnsupportedIntervals(int interval) =>
        Assert.Contains("intervalMinutes", MonitorRules.Validate("News", "https://example.org", interval).Keys);

    [Theory]
    [InlineData("ftp://example.org")]
    [InlineData("/relative")]
    [InlineData("https://user:password@example.org")]
    [InlineData("not a URL")]
    [InlineData("http://localhost/admin")]
    [InlineData("http://127.0.0.1/")]
    [InlineData("http://169.254.169.254/")]
    public void RejectsInvalidUrls(string url) =>
        Assert.Contains("url", MonitorRules.Validate("News", url, 60).Keys);

    [Fact]
    public void RejectsBlankAndLongNames()
    {
        Assert.Contains("name", MonitorRules.Validate("  ", "https://example.org", 60).Keys);
        Assert.Contains("name", MonitorRules.Validate(new string('a', 121), "https://example.org", 60).Keys);
        Assert.Contains("name", MonitorRules.Validate("Bad\r\nSubject", "https://example.org", 60).Keys);
    }

    [Fact]
    public void IntervalChangeSchedulesFromCurrentTime()
    {
        var now = new DateTime(2026, 10, 15, 12, 0, 0, DateTimeKind.Utc);
        Assert.Equal(now.AddHours(6), MonitorRules.NextCheckAt(now, 360));
    }
}
