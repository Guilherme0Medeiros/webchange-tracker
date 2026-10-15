using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using WebChangeRadar.Api.Features.Monitors;
using Xunit;

namespace WebChangeRadar.UnitTests;

public sealed class CheckScheduleTests
{
    [Theory]
    [InlineData(60)]
    [InlineData(360)]
    [InlineData(1440)]
    public void DevelopmentUsesAcceleratedIntervalWithoutChangingPreference(int intervalMinutes)
    {
        var now = new DateTime(2026, 10, 15, 12, 0, 0, DateTimeKind.Utc);
        var regularNext = now.AddMinutes(intervalMinutes);
        Assert.Equal(now.AddMinutes(2), Schedule(Environments.Development, "2")
            .NextCheckAt(regularNext, intervalMinutes));
        Assert.Equal(regularNext, Schedule(Environments.Production, "2")
            .NextCheckAt(regularNext, intervalMinutes));
        Assert.Equal(regularNext, Schedule(Environments.Development, null)
            .NextCheckAt(regularNext, intervalMinutes));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("60")]
    [InlineData("invalid")]
    public void InvalidOrNonAcceleratingValuesKeepRegularSchedule(string value)
    {
        var next = DateTime.UtcNow.AddHours(1);
        Assert.Equal(next, Schedule(Environments.Development, value).NextCheckAt(next, 60));
    }

    private static CheckSchedule Schedule(string environmentName, string? value)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["DevelopmentCheckIntervalMinutes"] = value }).Build();
        var environment = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        { EnvironmentName = environmentName }).Environment;
        return new CheckSchedule(configuration, environment);
    }
}
