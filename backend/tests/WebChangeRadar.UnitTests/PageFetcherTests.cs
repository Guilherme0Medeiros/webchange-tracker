using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using WebChangeRadar.Api.Services.Fetching;
using Xunit;

namespace WebChangeRadar.UnitTests;

public sealed class PageFetcherTests
{
    private static readonly IPAddress PublicAddress = IPAddress.Parse("93.184.216.34");

    [Theory]
    [InlineData("http://localhost/")]
    [InlineData("http://db.internal/")]
    [InlineData("http://printer.local/")]
    [InlineData("http://intranet/")]
    [InlineData("http://127.0.0.1/")]
    [InlineData("http://10.1.2.3/")]
    [InlineData("http://172.16.0.1/")]
    [InlineData("http://192.168.1.1/")]
    [InlineData("http://169.254.169.254/latest/meta-data/")]
    [InlineData("http://100.100.100.200/")]
    [InlineData("http://[::1]/")]
    [InlineData("http://[::ffff:127.0.0.1]/")]
    [InlineData("http://[fc00::1]/")]
    [InlineData("http://[fe80::1]/")]
    [InlineData("http://[2002:0a00:0001::]/")]
    [InlineData("file:///etc/passwd")]
    [InlineData("http://user:pass@example.org/")]
    public async Task RejectsUnsafeUrlsBeforeSending(string url)
    {
        var handler = new StubHandler((_, _) => throw new Exception("Should not send"));
        var result = await CreateFetcher(handler).FetchAsync(url);
        Assert.Equal("invalid_url", result.ErrorCode);
        Assert.Equal(0, handler.CallCount);
    }

    [Theory]
    [InlineData("10.0.0.1")]
    [InlineData("127.0.0.1")]
    [InlineData("169.254.169.254")]
    [InlineData("100.64.0.1")]
    [InlineData("192.0.2.1")]
    [InlineData("198.18.0.1")]
    [InlineData("198.51.100.1")]
    [InlineData("203.0.113.1")]
    [InlineData("224.0.0.1")]
    [InlineData("::ffff:10.0.0.1")]
    [InlineData("2001:db8::1")]
    public void RejectsUnsafeResolvedAddresses(string address) =>
        Assert.False(PublicUrlGuard.IsPublicAddress(IPAddress.Parse(address)));

    [Fact]
    public async Task FetchesHtmlAndFollowsSafeRelativeRedirect()
    {
        var handler = new StubHandler((request, _) => Task.FromResult(
            request.RequestUri!.AbsolutePath == "/start"
                ? Redirect("/final")
                : Html("<h1>Ready</h1>")));
        var result = await CreateFetcher(handler).FetchAsync("https://example.org/start");
        Assert.True(result.IsSuccess);
        Assert.Equal("<h1>Ready</h1>", result.Html);
        Assert.Equal("https://example.org/final", result.FinalUri!.AbsoluteUri);
        Assert.Equal(2, handler.CallCount);
    }

    [Fact]
    public async Task RejectsRedirectToPrivateDestinationWithoutSendingToIt()
    {
        var handler = new StubHandler((_, _) => Task.FromResult(Redirect("http://169.254.169.254/latest")));
        var result = await CreateFetcher(handler).FetchAsync("https://example.org/");
        Assert.Equal("unsafe_redirect", result.ErrorCode);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task RejectsRedirectWhoseDnsAnswerContainsPrivateAddress()
    {
        var resolver = new StubResolver(host => host == "other.example.org"
            ? [PublicAddress, IPAddress.Loopback] : [PublicAddress]);
        var handler = new StubHandler((_, _) => Task.FromResult(Redirect("https://other.example.org/")));
        var result = await CreateFetcher(handler, resolver).FetchAsync("https://example.org/");
        Assert.Equal("unsafe_destination", result.ErrorCode);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task RejectsPrivateDnsAnswerBeforeSending()
    {
        var handler = new StubHandler((_, _) => throw new Exception("Should not send"));
        var resolver = new StubResolver(_ => [IPAddress.Parse("10.0.0.8")]);
        var result = await CreateFetcher(handler, resolver).FetchAsync("https://example.org/");
        Assert.Equal("unsafe_destination", result.ErrorCode);
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task RejectsDnsRebindingOnNextResolution()
    {
        var count = 0;
        var guard = new PublicUrlGuard(new StubResolver(_ =>
            ++count == 1 ? [PublicAddress] : [IPAddress.Loopback]));
        await guard.ResolvePublicAddressesAsync("example.org", CancellationToken.None);
        await Assert.ThrowsAsync<UnsafeDestinationException>(() =>
            guard.ResolvePublicAddressesAsync("example.org", CancellationToken.None));
    }

    [Fact]
    public async Task ConfiguredHttpClientRejectsRebindingAtConnectionTime()
    {
        var count = 0;
        var services = new ServiceCollection();
        services.AddPageFetching();
        services.AddSingleton<IHostAddressResolver>(new StubResolver(_ =>
            Interlocked.Increment(ref count) == 1 ? [PublicAddress] : [IPAddress.Loopback]));
        using var provider = services.BuildServiceProvider();

        var result = await provider.GetRequiredService<IPageFetcher>()
            .FetchAsync("http://example.org/");

        Assert.Equal("unsafe_destination", result.ErrorCode);
        Assert.Equal(2, count);
    }

    [Fact]
    public async Task LimitsRedirects()
    {
        var handler = new StubHandler((_, _) => Task.FromResult(Redirect("/again")));
        var result = await CreateFetcher(handler).FetchAsync("https://example.org/");
        Assert.Equal("too_many_redirects", result.ErrorCode);
        Assert.Equal(PageFetcher.MaxRedirects + 1, handler.CallCount);
    }

    [Fact]
    public async Task RejectsLargeBodyEvenWithoutContentLength()
    {
        var response = Html(new string('a', PageFetcher.MaxResponseBytes + 1));
        response.Content.Headers.ContentLength = null;
        var handler = new StubHandler((_, _) => Task.FromResult(response));
        var result = await CreateFetcher(handler).FetchAsync("https://example.org/");
        Assert.Equal("response_too_large", result.ErrorCode);
    }

    [Fact]
    public async Task RejectsLargeDeclaredContentLength()
    {
        var response = Html("small");
        response.Content.Headers.ContentLength = PageFetcher.MaxResponseBytes + 1;
        var handler = new StubHandler((_, _) => Task.FromResult(response));
        var result = await CreateFetcher(handler).FetchAsync("https://example.org/");
        Assert.Equal("response_too_large", result.ErrorCode);
    }

    [Fact]
    public async Task ClassifiesHttpAndContentTypeErrors()
    {
        var http = await CreateFetcher(new StubHandler((_, _) => Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.NotFound)))).FetchAsync("https://example.org/");
        Assert.Equal("http_error", http.ErrorCode);
        Assert.Equal(HttpStatusCode.NotFound, http.StatusCode);

        var jsonResponse = Html("{}");
        jsonResponse.Content.Headers.ContentType = new("application/json");
        var json = await CreateFetcher(new StubHandler((_, _) => Task.FromResult(jsonResponse)))
            .FetchAsync("https://example.org/");
        Assert.Equal("invalid_content_type", json.ErrorCode);
    }

    [Fact]
    public async Task ClassifiesDnsAndConnectionFailures()
    {
        var dns = await CreateFetcher(new StubHandler((_, _) => throw new Exception()),
            new StubResolver(_ => throw new SocketException((int)SocketError.HostNotFound)))
            .FetchAsync("https://example.org/");
        Assert.Equal("dns_error", dns.ErrorCode);

        var connection = await CreateFetcher(new StubHandler((_, _) =>
            throw new HttpRequestException("failed", new SocketException((int)SocketError.ConnectionRefused))))
            .FetchAsync("https://example.org/");
        Assert.Equal("connection_error", connection.ErrorCode);
    }

    [Fact]
    public async Task HonorsCallerCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var handler = new StubHandler(async (_, token) =>
        {
            await Task.Delay(Timeout.Infinite, token);
            throw new Exception("Unreachable");
        });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            CreateFetcher(handler).FetchAsync("https://example.org/", cancellation.Token));
    }

    [Fact]
    public async Task TimesOutWhenServerDoesNotRespond()
    {
        var handler = new StubHandler(async (_, token) =>
        {
            await Task.Delay(Timeout.Infinite, token);
            throw new Exception("Unreachable");
        });
        var result = await CreateFetcher(handler).FetchAsync("https://example.org/");
        Assert.Equal("timeout", result.ErrorCode);
    }

    [Fact]
    public async Task ClassifiesUnsafeConnectionDestination()
    {
        var handler = new StubHandler((_, _) =>
            throw new HttpRequestException("blocked", new UnsafeDestinationException()));
        var result = await CreateFetcher(handler).FetchAsync("https://example.org/");
        Assert.Equal("unsafe_destination", result.ErrorCode);
    }

    private static PageFetcher CreateFetcher(StubHandler handler, StubResolver? resolver = null) =>
        new(new HttpClient(handler), new PublicUrlGuard(resolver ?? new StubResolver(_ => [PublicAddress])));

    private static HttpResponseMessage Html(string html) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(html, Encoding.UTF8, "text/html")
    };

    private static HttpResponseMessage Redirect(string location)
    {
        var response = new HttpResponseMessage(HttpStatusCode.Redirect);
        response.Headers.Location = new Uri(location, UriKind.RelativeOrAbsolute);
        return response;
    }

    private sealed class StubResolver(Func<string, IPAddress[]> resolve) : IHostAddressResolver
    {
        public Task<IPAddress[]> ResolveAsync(string host, CancellationToken cancellationToken) =>
            Task.FromResult(resolve(host));
    }

    private sealed class StubHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        public int CallCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            return send(request, cancellationToken);
        }
    }
}
