using Microsoft.EntityFrameworkCore;
using WebChangeRadar.Api.Data;
using Xunit;
using Monitor = WebChangeRadar.Api.Data.Monitor;

namespace WebChangeRadar.IntegrationTests;

public sealed class PersistenceTests(PostgreSqlFixture database) : IClassFixture<PostgreSqlFixture>
{
    [PostgreSqlFact]
    public async Task MigrationCreatesIdentityAndMonitorTablesWithEnforcedConstraints()
    {
        await using var context = database.CreateContext();
        Assert.Empty(await context.Database.GetPendingMigrationsAsync());
        Assert.Equal(3, (await context.Database.GetAppliedMigrationsAsync()).Count());

        var user = new ApplicationUser { UserName = "owner@example.test", Email = "owner@example.test" };
        context.Users.Add(user);
        await context.SaveChangesAsync();

        context.Monitors.Add(NewMonitor(user.Id, 60));
        await context.SaveChangesAsync();
        Assert.Equal(1, await context.Monitors.CountAsync(monitor => monitor.UserId == user.Id));

        await using var invalidIntervalContext = database.CreateContext();
        invalidIntervalContext.Monitors.Add(NewMonitor(user.Id, 15));
        await Assert.ThrowsAsync<DbUpdateException>(() => invalidIntervalContext.SaveChangesAsync());

        await using var missingOwnerContext = database.CreateContext();
        missingOwnerContext.Monitors.Add(NewMonitor("missing-owner", 60));
        await Assert.ThrowsAsync<DbUpdateException>(() => missingOwnerContext.SaveChangesAsync());
    }

    private static Monitor NewMonitor(string userId, int intervalMinutes) => new()
    {
        Id = Guid.NewGuid(),
        UserId = userId,
        Name = "Example",
        Url = "https://example.test",
        IntervalMinutes = intervalMinutes,
        NextCheckAt = DateTime.UtcNow.AddHours(1),
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
    };
}
