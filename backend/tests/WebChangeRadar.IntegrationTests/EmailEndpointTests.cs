using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using WebChangeRadar.Api.Features.Notifications;
using Xunit;

namespace WebChangeRadar.IntegrationTests;

public sealed class EmailEndpointTests(PostgreSqlFixture database, WebApplicationFactory<Program> baseFactory)
    : IClassFixture<PostgreSqlFixture>, IClassFixture<WebApplicationFactory<Program>>
{
    [PostgreSqlFact]
    public async Task ConfirmationRequiresCsrfAndChangesEligibilityOnlyAfterEmailLink()
    {
        var sender = new FakeSender();
        using var factory = baseFactory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureLogging(logging => logging.ClearProviders());
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
                services.Replace(ServiceDescriptor.Singleton<IChangeEmailSender>(sender));
            });
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:DefaultConnection"] = database.ConnectionString,
                    ["Email:PublicBaseUrl"] = "https://localhost"
                }));
        });
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false
        });
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.GetAsync("/api/notifications/email-status")).StatusCode);
        var csrf = await client.GetFromJsonAsync<JsonElement>("/api/auth/csrf");
        var token = csrf.GetProperty("requestToken").GetString()!;
        using var register = new HttpRequestMessage(HttpMethod.Post, "/api/auth/register")
        {
            Content = JsonContent.Create(new { email = $"confirmation-{Guid.NewGuid():N}@example.test",
                password = "SecurePassword123!" })
        };
        register.Headers.Add("X-CSRF-TOKEN", token);
        Assert.Equal(HttpStatusCode.Created, (await client.SendAsync(register)).StatusCode);
        var before = await client.GetFromJsonAsync<JsonElement>("/api/notifications/email-status");
        Assert.False(before.GetProperty("confirmed").GetBoolean());
        Assert.True(before.GetProperty("available").GetBoolean());
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PostAsync("/api/notifications/request-email-confirmation", null)).StatusCode);
        token = (await client.GetFromJsonAsync<JsonElement>("/api/auth/csrf"))
            .GetProperty("requestToken").GetString()!;
        using var request = new HttpRequestMessage(HttpMethod.Post,
            "/api/notifications/request-email-confirmation");
        request.Headers.Add("X-CSRF-TOKEN", token);
        Assert.Equal(HttpStatusCode.Accepted, (await client.SendAsync(request)).StatusCode);
        Assert.NotNull(sender.ConfirmationLink);
        Assert.Equal(HttpStatusCode.Redirect,
            (await client.GetAsync(new Uri(sender.ConfirmationLink!).PathAndQuery)).StatusCode);
        var after = await client.GetFromJsonAsync<JsonElement>("/api/notifications/email-status");
        Assert.True(after.GetProperty("confirmed").GetBoolean());
    }

    private sealed class FakeSender : IChangeEmailSender
    {
        public bool IsConfigured => true;
        public string? ConfirmationLink { get; private set; }
        public Task SendAsync(ChangeEmail email, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task SendConfirmationAsync(string recipient, string link, CancellationToken cancellationToken)
        {
            ConfirmationLink = link;
            return Task.CompletedTask;
        }
    }
}
