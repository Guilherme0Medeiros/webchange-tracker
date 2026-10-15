using Xunit;
using WebChangeRadar.Api.Configuration;

namespace WebChangeRadar.IntegrationTests;

public sealed class PostgreSqlFactAttribute : FactAttribute
{
    public PostgreSqlFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(LocalEnvFile.GetValue("WEBCHANGE_TEST_CONNECTION")))
        {
            Skip = "Set WEBCHANGE_TEST_CONNECTION to run PostgreSQL integration tests.";
        }
    }
}
