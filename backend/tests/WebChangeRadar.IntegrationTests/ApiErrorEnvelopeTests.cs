using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Xunit;

namespace WebChangeRadar.IntegrationTests;

public sealed class ApiErrorEnvelopeTests(WebApplicationFactory<Program> baseFactory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task FrameworkErrorsUsePortugueseProblemDetails()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        using var missing = await client.GetAsync("/api/unknown");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal("Recurso não encontrado.",
            (await missing.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("title").GetString());

        using var malformed = await client.PostAsync("/api/auth/login",
            new StringContent("{", Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, malformed.StatusCode);
        var problem = await malformed.Content.ReadFromJsonAsync<JsonElement>();
        Assert.StartsWith("Solicitação inválida", problem.GetProperty("title").GetString());
        Assert.False(problem.TryGetProperty("detail", out _));
    }

    [Fact]
    public async Task UnexpectedErrorsDoNotExposeInternalDetails()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/api/notifications/confirm-email?userId=unknown&token=invalid");
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Não foi possível concluir a solicitação. Tente novamente mais tarde.",
            problem.GetProperty("title").GetString());
        Assert.False(problem.TryGetProperty("detail", out _));
    }

    private WebApplicationFactory<Program> CreateFactory() => baseFactory.WithWebHostBuilder(builder =>
    {
        builder.ConfigureLogging(logging => logging.ClearProviders());
        builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] =
                    "Host=127.0.0.1;Port=1;Database=unavailable;Username=test;Password=test;Timeout=1"
            }));
    });
}
