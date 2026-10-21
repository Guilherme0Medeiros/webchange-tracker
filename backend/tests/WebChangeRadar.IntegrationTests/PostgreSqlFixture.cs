using Microsoft.EntityFrameworkCore;
using Npgsql;
using WebChangeRadar.Api.Data;
using WebChangeRadar.Api.Configuration;
using Xunit;

namespace WebChangeRadar.IntegrationTests;

public sealed class PostgreSqlFixture : IAsyncLifetime
{
    private readonly string _schemaName = $"webchange_test_{Guid.NewGuid():N}";
    private string? _baseConnectionString;
    private bool _schemaCreated;

    public string ConnectionString { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        _baseConnectionString = LocalEnvFile.GetValue("WEBCHANGE_TEST_CONNECTION")
            ?? throw new InvalidOperationException("WEBCHANGE_TEST_CONNECTION is required.");

        var builder = new NpgsqlConnectionStringBuilder(_baseConnectionString);
        if (string.IsNullOrWhiteSpace(builder.Database))
            throw new InvalidOperationException("WEBCHANGE_TEST_CONNECTION must name a test database.");
        var developmentConnection = LocalEnvFile.GetValue("ConnectionStrings__DefaultConnection");
        if (!string.IsNullOrWhiteSpace(developmentConnection))
        {
            var development = new NpgsqlConnectionStringBuilder(developmentConnection);
            // Different host spellings (for example localhost and 127.0.0.1) may
            // still point at the same database. Require a distinct database name.
            if (string.Equals(builder.Database, development.Database, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Test and development connections must use different databases.");
        }

        await using (var connection = new NpgsqlConnection(_baseConnectionString))
        {
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand($"CREATE SCHEMA \"{_schemaName}\"", connection);
            await command.ExecuteNonQueryAsync();
            _schemaCreated = true;
        }

        builder.SearchPath = _schemaName;
        builder.Pooling = false;
        ConnectionString = builder.ConnectionString;

        await using var context = CreateContext();
        await context.Database.MigrateAsync();
    }

    public ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(ConnectionString)
            .Options;
        return new ApplicationDbContext(options);
    }

    public async Task DisposeAsync()
    {
        if (!_schemaCreated || _baseConnectionString is null)
        {
            return;
        }

        await using var connection = new NpgsqlConnection(_baseConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"DROP SCHEMA IF EXISTS \"{_schemaName}\" CASCADE", connection);
        await command.ExecuteNonQueryAsync();
    }
}
