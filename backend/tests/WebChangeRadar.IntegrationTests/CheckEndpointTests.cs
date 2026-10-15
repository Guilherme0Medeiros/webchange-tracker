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
using WebChangeRadar.Api.Services.Fetching;
using Xunit;

namespace WebChangeRadar.IntegrationTests;

public sealed class CheckEndpointTests(PostgreSqlFixture database, WebApplicationFactory<Program> baseFactory)
    : IClassFixture<PostgreSqlFixture>, IClassFixture<WebApplicationFactory<Program>>
{
    [PostgreSqlFact]
    public async Task ZeroManualCooldownOnlyAppliesInDevelopment()
    {
        await AssertSecondRequest("Development", HttpStatusCode.Accepted);
        await AssertSecondRequest("Production", (HttpStatusCode)429);

        async Task AssertSecondRequest(string environment, HttpStatusCode expected)
        {
            using var factory = baseFactory.WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment(environment);
                builder.ConfigureLogging(logging => logging.ClearProviders());
                builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
                    new Dictionary<string, string?>
                    {
                        ["ConnectionStrings:DefaultConnection"] = database.ConnectionString,
                        ["ManualCheckCooldownMinutes"] = "0"
                    }));
                builder.ConfigureTestServices(services =>
                    services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider()));
            });
            using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
                { BaseAddress = new Uri("https://localhost") });
            await Register(client);
            using var created = await WithCsrf(client, HttpMethod.Post, "/api/monitors",
                new { name = "Example", url = "https://example.org", intervalMinutes = 60 });
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
            using var first = await WithCsrf(client, HttpMethod.Post, $"/api/monitors/{id}/checks");
            Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
            using var second = await WithCsrf(client, HttpMethod.Post, $"/api/monitors/{id}/checks");
            Assert.Equal(expected, second.StatusCode);
        }
    }

    [PostgreSqlFact]
    public async Task ManualQueueHistoryDiffAndNotificationsArePrivate()
    {
        var fetcher = new StubFetcher();
        using var factory = baseFactory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureLogging(logging => logging.ClearProviders());
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
                new Dictionary<string, string?> { ["ConnectionStrings:DefaultConnection"] = database.ConnectionString,
                    ["InternalJobs:Token"] = "integration-secret" }));
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
                services.RemoveAll<IPageFetcher>();
                services.AddSingleton<IPageFetcher>(fetcher);
            });
        });
        using var owner = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        using var other = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        await Register(owner);
        await Register(other);
        using var created = await WithCsrf(owner, HttpMethod.Post, "/api/monitors",
            new { name = "Example", url = "https://example.org", intervalMinutes = 60 });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        using var missingToken = await owner.PostAsync($"/api/monitors/{id}/checks", null);
        Assert.Equal(HttpStatusCode.BadRequest, missingToken.StatusCode);
        using var first = await WithCsrf(owner, HttpMethod.Post, $"/api/monitors/{id}/checks");
        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
        using var limited = await WithCsrf(owner, HttpMethod.Post, $"/api/monitors/{id}/checks");
        Assert.Equal((HttpStatusCode)429, limited.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/monitors/{id}/checks")).StatusCode);
        using var forbiddenQueue = await WithCsrf(other, HttpMethod.Post, $"/api/monitors/{id}/checks");
        Assert.Equal(HttpStatusCode.NotFound, forbiddenQueue.StatusCode);

        using var denied = await owner.PostAsync("/internal/jobs/process-due", null);
        Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
        await RunJob(owner);
        using var checks = await owner.GetAsync($"/api/monitors/{id}/checks");
        Assert.Equal(HttpStatusCode.OK, checks.StatusCode);
        var checksJson = await checks.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(checksJson.GetProperty("totalCount").GetInt32() >= 1);
        Assert.Equal(0, (await (await owner.GetAsync("/api/notifications")).Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("totalCount").GetInt32());

        fetcher.Html = "<p>Changed</p>";
        await using (var db = database.CreateContext())
        {
            db.CheckRuns.Add(new WebChangeRadar.Api.Data.CheckRun { Id = Guid.NewGuid(), MonitorId = id,
                RequestedAt = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }
        await RunJob(owner);
        var changes = await (await owner.GetAsync($"/api/monitors/{id}/changes?pageSize=1"))
            .Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, changes.GetProperty("totalCount").GetInt32());
        var changeId = changes.GetProperty("items")[0].GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/changes/{changeId}")).StatusCode);
        var diff = await (await owner.GetAsync($"/api/changes/{changeId}"))
            .Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains("Changed", diff.GetProperty("added").ToString());
        var notices = await (await owner.GetAsync("/api/notifications"))
            .Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, notices.GetProperty("totalCount").GetInt32());
        var noticeId = notices.GetProperty("items")[0].GetProperty("id").GetGuid();
        Assert.Equal(0, (await (await other.GetAsync("/api/notifications"))
            .Content.ReadFromJsonAsync<JsonElement>()).GetProperty("totalCount").GetInt32());
        using var wrongRead = await WithCsrf(other, HttpMethod.Patch, $"/api/notifications/{noticeId}/read");
        Assert.Equal(HttpStatusCode.NotFound, wrongRead.StatusCode);
        using var read = await WithCsrf(owner, HttpMethod.Patch, $"/api/notifications/{noticeId}/read");
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        Assert.Equal(0, (await (await owner.GetAsync("/api/notifications?unread=true"))
            .Content.ReadFromJsonAsync<JsonElement>()).GetProperty("totalCount").GetInt32());

        async Task RunJob(HttpClient client)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/internal/jobs/process-due");
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "integration-secret");
            using var response = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }

    private static async Task Register(HttpClient client)
    {
        using var response = await WithCsrf(client, HttpMethod.Post, "/api/auth/register",
            new { email = $"checks-{Guid.NewGuid():N}@example.test", password = "SecurePassword123!" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    private static async Task<HttpResponseMessage> WithCsrf(HttpClient client, HttpMethod method,
        string path, object? body = null)
    {
        var token = (await (await client.GetAsync("/api/auth/csrf")).Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("requestToken").GetString();
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Add("X-CSRF-TOKEN", token);
        if (body is not null) request.Content = JsonContent.Create(body);
        return await client.SendAsync(request);
    }

    private sealed class StubFetcher : IPageFetcher
    {
        public string Html { get; set; } = "<p>Original</p>";
        public Task<PageFetchResult> FetchAsync(string url, CancellationToken cancellationToken = default) =>
            Task.FromResult(new PageFetchResult(Html, new Uri(url), null, HttpStatusCode.OK));
    }
}
