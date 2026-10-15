using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using WebChangeRadar.Api.Data;
using Xunit;
using Monitor = WebChangeRadar.Api.Data.Monitor;

namespace WebChangeRadar.UnitTests;

public sealed class MonitorModelTests
{
    [Fact]
    public void MonitorModelDefinesOwnershipIndexesAndDatabaseConstraints()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=localhost;Database=model_only;Username=unused;Password=unused")
            .Options;

        using var context = new ApplicationDbContext(options);
        var monitor = context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(Monitor))!;

        Assert.Equal("Monitors", monitor.GetTableName());
        Assert.Equal(2, monitor.GetIndexes().Count());
        Assert.Contains(monitor.GetIndexes(), index =>
            index.Properties.Select(property => property.Name).SequenceEqual(["UserId", "Status"]));
        Assert.Contains(monitor.GetIndexes(), index =>
            index.Properties.Select(property => property.Name).SequenceEqual(["NextCheckAt"]));
        Assert.Equal("UserId", monitor.GetForeignKeys().Single().Properties.Single().Name);
        Assert.Contains(monitor.GetCheckConstraints(), constraint => constraint.Name == "CK_Monitors_IntervalMinutes");
        Assert.Contains(monitor.GetCheckConstraints(), constraint => constraint.Name == "CK_Monitors_Status");
    }

    [Fact]
    public void MonitorMigrationGeneratesPostgreSqlSchemaChanges()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=localhost;Database=model_only;Username=unused;Password=unused")
            .Options;
        using var context = new ApplicationDbContext(options);
        var sql = context.GetService<IMigrator>().GenerateScript();

        Assert.Contains("BaselineResetAt", sql);
        Assert.Contains("NextCheckAt", sql);
        Assert.Contains("DROP NOT NULL", sql);
    }
}
