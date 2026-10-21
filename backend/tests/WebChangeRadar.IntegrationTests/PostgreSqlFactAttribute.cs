using Xunit;

namespace WebChangeRadar.IntegrationTests;

// The fixture fails explicitly when the test database is unavailable.
public sealed class PostgreSqlFactAttribute : FactAttribute
{
}
