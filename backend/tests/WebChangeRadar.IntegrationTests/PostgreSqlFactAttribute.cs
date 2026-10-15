using Xunit;

namespace WebChangeRadar.IntegrationTests;

public sealed class PostgreSqlFactAttribute : FactAttribute
{
    public PostgreSqlFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("WEBCHANGE_TEST_CONNECTION")))
        {
            Skip = "Set WEBCHANGE_TEST_CONNECTION to run PostgreSQL integration tests.";
        }
    }
}
