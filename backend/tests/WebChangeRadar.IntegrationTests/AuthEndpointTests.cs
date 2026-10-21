using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace WebChangeRadar.IntegrationTests;

public sealed class AuthEndpointTests(
    PostgreSqlFixture database,
    WebApplicationFactory<Program> baseFactory)
    : IClassFixture<PostgreSqlFixture>, IClassFixture<WebApplicationFactory<Program>>
{
    private const string Password = "SecurePassword123!";

    [PostgreSqlFact]
    public async Task RegistrationCreatesSessionAndLogoutRevokesIt()
    {
        using var factory = CreateFactory();
        using var client = CreateClient(factory);
        var email = UniqueEmail();

        using var unauthenticated = await client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, unauthenticated.StatusCode);
        Assert.Equal("Entre na sua conta para continuar.",
            (await unauthenticated.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("title").GetString());
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PostAsJsonAsync("/api/auth/register", new { email, password = Password })).StatusCode);

        var token = await GetCsrfTokenAsync(client, assertCookie: true);
        using var unauthorizedLogout = await PostWithCsrfAsync(client, "/api/auth/logout", new { }, token);
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorizedLogout.StatusCode);
        using var registration = await PostWithCsrfAsync(client, "/api/auth/register", new { email, password = Password }, token);
        Assert.Equal(HttpStatusCode.Created, registration.StatusCode);
        AssertCookieIsSecure(registration, "__Host-WebChangeRadar.Auth");

        using var me = await client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        Assert.Equal(email, (await me.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("email").GetString());

        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PostAsync("/api/auth/logout", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/auth/me")).StatusCode);

        token = await GetCsrfTokenAsync(client);
        using var logout = await PostWithCsrfAsync(client, "/api/auth/logout", new { }, token);
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);
    }

    [PostgreSqlFact]
    public async Task LoginRejectsInvalidCredentialsAndAcceptsValidPassword()
    {
        using var factory = CreateFactory();
        using var client = CreateClient(factory);
        var email = UniqueEmail();
        var token = await GetCsrfTokenAsync(client);
        using var registration = await PostWithCsrfAsync(client, "/api/auth/register", new { email, password = Password }, token);
        Assert.Equal(HttpStatusCode.Created, registration.StatusCode);

        token = await GetCsrfTokenAsync(client);
        using var logout = await PostWithCsrfAsync(client, "/api/auth/logout", new { }, token);
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);

        token = await GetCsrfTokenAsync(client);
        using var wrongPassword = await PostWithCsrfAsync(client, "/api/auth/login", new { email, password = "wrong" }, token);
        Assert.Equal(HttpStatusCode.Unauthorized, wrongPassword.StatusCode);
        Assert.Equal("E-mail ou senha inválidos.",
            (await wrongPassword.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("title").GetString());
        using var unknownUser = await PostWithCsrfAsync(client, "/api/auth/login", new { email = UniqueEmail(), password = Password }, token);
        Assert.Equal(HttpStatusCode.Unauthorized, unknownUser.StatusCode);
        Assert.Equal("E-mail ou senha inválidos.",
            (await unknownUser.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("title").GetString());
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);

        using var login = await PostWithCsrfAsync(client, "/api/auth/login", new { email, password = Password }, token);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        AssertCookieIsSecure(login, "__Host-WebChangeRadar.Auth");
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/auth/me")).StatusCode);
    }

    [PostgreSqlFact]
    public async Task RegistrationValidatesEmailPasswordAndDuplicateAccount()
    {
        using var factory = CreateFactory();
        using var client = CreateClient(factory);
        var token = await GetCsrfTokenAsync(client);

        using var invalidEmail = await PostWithCsrfAsync(client, "/api/auth/register", new { email = "bad-email", password = Password }, token);
        Assert.Equal(HttpStatusCode.BadRequest, invalidEmail.StatusCode);
        Assert.Equal("Informe um e-mail válido com até 254 caracteres.",
            (await invalidEmail.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors").GetProperty("email")[0].GetString());

        var email = UniqueEmail();
        using var weakPassword = await PostWithCsrfAsync(client, "/api/auth/register", new { email, password = "weak" }, token);
        Assert.Equal(HttpStatusCode.BadRequest, weakPassword.StatusCode);
        var passwordErrors = (await weakPassword.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("errors").GetProperty("password").EnumerateArray()
            .Select(message => message.GetString()).ToArray();
        Assert.Contains("A senha deve ter pelo menos 8 caracteres.", passwordErrors);
        Assert.Contains("A senha deve conter pelo menos uma letra maiúscula.", passwordErrors);
        Assert.Contains("A senha deve conter pelo menos um número.", passwordErrors);
        Assert.Contains("A senha deve conter pelo menos um símbolo, como ! ou #.", passwordErrors);

        using var noLowercase = await PostWithCsrfAsync(client, "/api/auth/register",
            new { email, password = "ABCDEFG1!" }, token);
        Assert.Contains("A senha deve conter pelo menos uma letra minúscula.",
            (await noLowercase.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors")
                .GetProperty("password").EnumerateArray().Select(message => message.GetString()));

        using var missingPassword = await PostWithCsrfAsync(client, "/api/auth/register", new { email, password = "" }, token);
        Assert.Equal("Informe uma senha.",
            (await missingPassword.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors").GetProperty("password")[0].GetString());

        const string eightCharacterPassword = "Abcdef1!";
        using var registration = await PostWithCsrfAsync(client, "/api/auth/register", new { email, password = eightCharacterPassword }, token);
        Assert.Equal(HttpStatusCode.Created, registration.StatusCode);

        token = await GetCsrfTokenAsync(client);
        using var duplicate = await PostWithCsrfAsync(client, "/api/auth/register", new { email, password = eightCharacterPassword }, token);
        Assert.Equal(HttpStatusCode.BadRequest, duplicate.StatusCode);
        Assert.Equal("Não foi possível criar a conta com os dados informados.",
            (await duplicate.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors").GetProperty("email")[0].GetString());
    }

    [PostgreSqlFact]
    public async Task LoginRateLimitRejectsExcessiveAttempts()
    {
        using var factory = CreateFactory();
        using var client = CreateClient(factory);
        var token = await GetCsrfTokenAsync(client);

        for (var attempt = 0; attempt < 10; attempt++)
        {
            using var response = await PostWithCsrfAsync(client, "/api/auth/login",
                new { email = UniqueEmail(), password = Password }, token);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        using var blocked = await PostWithCsrfAsync(client, "/api/auth/login",
            new { email = UniqueEmail(), password = Password }, token);
        Assert.Equal(HttpStatusCode.TooManyRequests, blocked.StatusCode);
        Assert.Equal("Muitas solicitações. Aguarde e tente novamente.",
            (await blocked.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("title").GetString());
    }

    private WebApplicationFactory<Program> CreateFactory() => baseFactory.WithWebHostBuilder(builder =>
    {
        builder.ConfigureLogging(logging => logging.ClearProviders());
        builder.ConfigureTestServices(services =>
            services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider()));
        builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = database.ConnectionString
            }));
    });

    private static HttpClient CreateClient(WebApplicationFactory<Program> factory) => factory.CreateClient(
        new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });

    private static async Task<string> GetCsrfTokenAsync(HttpClient client, bool assertCookie = false)
    {
        using var response = await client.GetAsync("/api/auth/csrf");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        if (assertCookie)
        {
            AssertCookieIsSecure(response, "__Host-WebChangeRadar.Csrf");
        }
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("requestToken").GetString()!;
    }

    private static async Task<HttpResponseMessage> PostWithCsrfAsync(
        HttpClient client, string path, object body, string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = JsonContent.Create(body)
        };
        request.Headers.Add("X-CSRF-TOKEN", token);
        return await client.SendAsync(request);
    }

    private static void AssertCookieIsSecure(HttpResponseMessage response, string name)
    {
        Assert.True(response.Headers.TryGetValues("Set-Cookie", out var cookies));
        var cookie = Assert.Single(cookies!, value => value.StartsWith(name + "=", StringComparison.Ordinal));
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);
    }

    private static string UniqueEmail() => $"user-{Guid.NewGuid():N}@example.test";
}
