using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace WebChangeRadar.IntegrationTests;

public sealed class MonitorEndpointTests(PostgreSqlFixture database, WebApplicationFactory<Program> baseFactory)
    : IClassFixture<PostgreSqlFixture>, IClassFixture<WebApplicationFactory<Program>>
{
    private const string Password = "SecurePassword123!";

    [PostgreSqlFact]
    public async Task ConcurrentCreationsRespectPerUserLimit()
    {
        using var factory = CreateFactory();
        using var client = CreateClient(factory);
        await RegisterAsync(client);
        var csrf = await client.GetFromJsonAsync<JsonElement>("/api/auth/csrf");
        var token = csrf.GetProperty("requestToken").GetString()!;

        var responses = await Task.WhenAll(Enumerable.Range(0, 5).Select(async index =>
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/monitors")
            {
                Content = JsonContent.Create(new { name = $"Concurrent {index}",
                    url = $"https://example.org/{index}", intervalMinutes = 60 })
            };
            request.Headers.Add("X-CSRF-TOKEN", token);
            return await client.SendAsync(request);
        }));
        try
        {
            Assert.Equal(3, responses.Count(response => response.StatusCode == HttpStatusCode.Created));
            Assert.Equal(2, responses.Count(response => response.StatusCode == HttpStatusCode.Conflict));
            foreach (var limited in responses.Where(response => response.StatusCode == HttpStatusCode.Conflict))
            {
                var problem = await limited.Content.ReadFromJsonAsync<JsonElement>();
                Assert.Equal("Limite de monitoramentos atingido.", problem.GetProperty("title").GetString());
                Assert.Equal("Você pode ter até 3 monitoramentos.", problem.GetProperty("detail").GetString());
            }
            var list = await client.GetFromJsonAsync<JsonElement>("/api/monitors");
            Assert.Equal(3, list.GetProperty("totalCount").GetInt32());
        }
        finally
        {
            foreach (var response in responses) response.Dispose();
        }
    }

    [PostgreSqlFact]
    public async Task CrudStatusLimitAndPaging()
    {
        using var factory = CreateFactory();
        using var client = CreateClient(factory);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/monitors")).StatusCode);
        await RegisterAsync(client);

        var before = DateTime.UtcNow;
        var first = await CreateMonitorAsync(client, "First", "https://example.org/one", 60);
        Assert.Equal("active", first.GetProperty("status").GetString());
        Assert.InRange(first.GetProperty("nextCheckAt").GetDateTime(), before, DateTime.UtcNow);
        var id = first.GetProperty("id").GetGuid();
        await CreateMonitorAsync(client, "Second", "https://example.org/two", 360);
        await CreateMonitorAsync(client, "Third", "https://example.org/three", 1440);

        using var overLimit = await SendWithCsrfAsync(client, HttpMethod.Post, "/api/monitors",
            new { name = "Fourth", url = "https://example.org/four", intervalMinutes = 60 });
        Assert.Equal(HttpStatusCode.Conflict, overLimit.StatusCode);

        using var page = await client.GetAsync("/api/monitors?page=2&pageSize=2&status=active");
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        var pageJson = await page.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(3, pageJson.GetProperty("totalCount").GetInt32());
        Assert.Single(pageJson.GetProperty("items").EnumerateArray());

        using var update = await SendWithCsrfAsync(client, HttpMethod.Put, $"/api/monitors/{id}",
            new { name = "Updated", url = "https://example.org/one", intervalMinutes = 360 });
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        var updated = await update.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Updated", updated.GetProperty("name").GetString());
        Assert.InRange(updated.GetProperty("nextCheckAt").GetDateTime(),
            DateTime.UtcNow.AddHours(6).AddMinutes(-1), DateTime.UtcNow.AddHours(6).AddMinutes(1));
        await using (var db = database.CreateContext())
            Assert.Null((await db.Monitors.SingleAsync(m => m.Id == id)).BaselineResetAt);

        using var urlUpdate = await SendWithCsrfAsync(client, HttpMethod.Put, $"/api/monitors/{id}",
            new { name = "Updated", url = "https://example.org/new", intervalMinutes = 360 });
        Assert.Equal(HttpStatusCode.OK, urlUpdate.StatusCode);
        var urlUpdated = await urlUpdate.Content.ReadFromJsonAsync<JsonElement>();
        Assert.InRange(urlUpdated.GetProperty("nextCheckAt").GetDateTime(), before, DateTime.UtcNow);
        await using (var db = database.CreateContext())
            Assert.InRange((await db.Monitors.SingleAsync(m => m.Id == id)).BaselineResetAt!.Value,
                before, DateTime.UtcNow);

        using var pause = await SendWithCsrfAsync(client, HttpMethod.Patch, $"/api/monitors/{id}/status",
            new { status = "paused" });
        Assert.Equal(HttpStatusCode.OK, pause.StatusCode);
        var paused = await pause.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("paused", paused.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, paused.GetProperty("nextCheckAt").ValueKind);
        using var pausedPage = await client.GetAsync("/api/monitors?status=paused");
        Assert.Equal(1, (await pausedPage.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("totalCount").GetInt32());

        using var resume = await SendWithCsrfAsync(client, HttpMethod.Patch, $"/api/monitors/{id}/status",
            new { status = "active" });
        var resumed = await resume.Content.ReadFromJsonAsync<JsonElement>();
        Assert.InRange(resumed.GetProperty("nextCheckAt").GetDateTime(), before, DateTime.UtcNow);

        using var deleted = await SendWithCsrfAsync(client, HttpMethod.Delete, $"/api/monitors/{id}");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        using var missing = await client.GetAsync($"/api/monitors/{id}");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal("Monitoramento não encontrado.",
            (await missing.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("title").GetString());
        await CreateMonitorAsync(client, "Replacement", "https://example.org/replacement", 60);
    }

    [PostgreSqlFact]
    public async Task UsersCannotReadChangeOrDeleteOthersMonitors()
    {
        using var factory = CreateFactory();
        using var owner = CreateClient(factory);
        using var other = CreateClient(factory);
        await RegisterAsync(owner);
        await RegisterAsync(other);
        var monitor = await CreateMonitorAsync(owner, "Private", "https://example.org", 60);
        var id = monitor.GetProperty("id").GetGuid();

        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/monitors/{id}")).StatusCode);
        using var list = await other.GetAsync("/api/monitors");
        Assert.Equal(0, (await list.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("totalCount").GetInt32());
        using var update = await SendWithCsrfAsync(other, HttpMethod.Put, $"/api/monitors/{id}",
            new { name = "Changed", url = "https://example.org", intervalMinutes = 60 });
        Assert.Equal(HttpStatusCode.NotFound, update.StatusCode);
        using var pause = await SendWithCsrfAsync(other, HttpMethod.Patch, $"/api/monitors/{id}/status",
            new { status = "paused" });
        Assert.Equal(HttpStatusCode.NotFound, pause.StatusCode);
        using var delete = await SendWithCsrfAsync(other, HttpMethod.Delete, $"/api/monitors/{id}");
        Assert.Equal(HttpStatusCode.NotFound, delete.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync($"/api/monitors/{id}")).StatusCode);
    }

    [PostgreSqlFact]
    public async Task ChangingSelectorStartsNewBaselineAndKeepsPriorSnapshot()
    {
        using var factory = CreateFactory();
        using var client = CreateClient(factory);
        await RegisterAsync(client);
        var monitor = await CreateMonitorAsync(client, "Selected", "https://example.org/page", 60);
        var id = monitor.GetProperty("id").GetGuid();
        var snapshotId = Guid.NewGuid();
        await using (var db = database.CreateContext())
        {
            db.Snapshots.Add(new WebChangeRadar.Api.Data.Snapshot { Id = snapshotId, MonitorId = id,
                ContentHash = new string('a', 64), NormalizedText = "Earlier", CapturedAt = DateTime.UtcNow });
            (await db.Monitors.SingleAsync(x => x.Id == id)).CurrentSnapshotId = snapshotId;
            await db.SaveChangesAsync();
        }
        using var response = await SendWithCsrfAsync(client, HttpMethod.Put, $"/api/monitors/{id}",
            new { name = "Selected", url = "https://example.org/page", intervalMinutes = 60,
                contentSelector = "main", keywords = new[] { "launch" } });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("main", body.GetProperty("contentSelector").GetString());
        Assert.Equal("launch", body.GetProperty("keywords")[0].GetString());
        await using var saved = database.CreateContext();
        var updated = await saved.Monitors.SingleAsync(x => x.Id == id);
        Assert.Null(updated.CurrentSnapshotId);
        Assert.NotNull(updated.BaselineResetAt);
        Assert.True(await saved.Snapshots.AnyAsync(x => x.Id == snapshotId));
        updated.CurrentSnapshotId = snapshotId;
        await saved.SaveChangesAsync();

        using var keywordOnly = await SendWithCsrfAsync(client, HttpMethod.Put, $"/api/monitors/{id}",
            new { name = "Selected", url = "https://example.org/page", intervalMinutes = 60,
                contentSelector = "main", keywords = new[] { "ALERTA", " alerta " }, emailAlertsEnabled = true });
        Assert.Equal(HttpStatusCode.OK, keywordOnly.StatusCode);
        var keywordResponse = await keywordOnly.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Single(keywordResponse.GetProperty("keywords").EnumerateArray());
        Assert.True(keywordResponse.GetProperty("emailAlertsEnabled").GetBoolean());
        await using var keywordDb = database.CreateContext();
        Assert.Equal(snapshotId, (await keywordDb.Monitors.SingleAsync(x => x.Id == id)).CurrentSnapshotId);
    }

    [PostgreSqlFact]
    public async Task ValidatesInputPagingAndCsrf()
    {
        using var factory = CreateFactory();
        using var client = CreateClient(factory);
        await RegisterAsync(client);
        using var invalid = await SendWithCsrfAsync(client, HttpMethod.Post, "/api/monitors",
            new { name = " ", url = "ftp://example.org", intervalMinutes = 30 });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        var errors = (await invalid.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors");
        Assert.True(errors.TryGetProperty("name", out _));
        Assert.True(errors.TryGetProperty("url", out _));
        Assert.True(errors.TryGetProperty("intervalMinutes", out _));
        Assert.Equal("Informe uma URL pública HTTP ou HTTPS, sem usuário ou senha, com até 2.048 caracteres.",
            errors.GetProperty("url")[0].GetString());
        using var invalidSelector = await SendWithCsrfAsync(client, HttpMethod.Post, "/api/monitors",
            new { name = "Valid", url = "https://example.org", intervalMinutes = 60,
                contentSelector = "main[", keywords = Enumerable.Range(0, 11).Select(x => $"word{x}").ToArray() });
        Assert.Equal(HttpStatusCode.BadRequest, invalidSelector.StatusCode);
        var selectorErrors = (await invalidSelector.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors");
        Assert.True(selectorErrors.TryGetProperty("contentSelector", out _));
        Assert.Equal("Informe um seletor CSS válido com até 160 caracteres.",
            selectorErrors.GetProperty("contentSelector")[0].GetString());
        Assert.True(selectorErrors.TryGetProperty("keywords", out _));
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/monitors?page=0")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/monitors?pageSize=101")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/monitors?status=unknown")).StatusCode);
        using var noCsrf = await client.PostAsJsonAsync("/api/monitors",
            new { name = "Valid", url = "https://example.org", intervalMinutes = 60 });
        Assert.Equal(HttpStatusCode.BadRequest, noCsrf.StatusCode);
        Assert.Equal("Não foi possível validar a solicitação. Atualize a página e tente novamente.",
            (await noCsrf.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("title").GetString());
    }

    private WebApplicationFactory<Program> CreateFactory() => baseFactory.WithWebHostBuilder(builder =>
    {
        builder.ConfigureLogging(logging => logging.ClearProviders());
        builder.ConfigureTestServices(services =>
            services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider()));
        builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = database.ConnectionString,
                ["DevelopmentCheckIntervalMinutes"] = ""
            }));
    });

    private static HttpClient CreateClient(WebApplicationFactory<Program> factory) => factory.CreateClient(
        new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });

    private static async Task RegisterAsync(HttpClient client)
    {
        using var response = await SendWithCsrfAsync(client, HttpMethod.Post, "/api/auth/register",
            new { email = $"monitor-{Guid.NewGuid():N}@example.test", password = Password });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    private static async Task<JsonElement> CreateMonitorAsync(HttpClient client, string name, string url, int interval)
    {
        using var response = await SendWithCsrfAsync(client, HttpMethod.Post, "/api/monitors",
            new { name, url, intervalMinutes = interval });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.StartsWith("/api/monitors/", response.Headers.Location?.ToString());
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static async Task<HttpResponseMessage> SendWithCsrfAsync(
        HttpClient client, HttpMethod method, string path, object? body = null)
    {
        using var tokenResponse = await client.GetAsync("/api/auth/csrf");
        var token = (await tokenResponse.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("requestToken").GetString();
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Add("X-CSRF-TOKEN", token);
        if (body is not null) request.Content = JsonContent.Create(body);
        return await client.SendAsync(request);
    }
}
