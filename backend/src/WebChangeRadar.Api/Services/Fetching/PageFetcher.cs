using System.Net;
using System.Net.Sockets;
using System.Text;

namespace WebChangeRadar.Api.Services.Fetching;

public interface IPageFetcher
{
    Task<PageFetchResult> FetchAsync(string url, CancellationToken cancellationToken = default);
}

public sealed record PageFetchResult(string? Html, Uri? FinalUri, string? ErrorCode, HttpStatusCode? StatusCode)
{
    public bool IsSuccess => ErrorCode is null;
}

public sealed class PageFetcher(HttpClient client, PublicUrlGuard guard) : IPageFetcher
{
    public const int MaxResponseBytes = 512 * 1024;
    public const int MaxRedirects = 5;
    public static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);

    public async Task<PageFetchResult> FetchAsync(string url, CancellationToken cancellationToken = default)
    {
        if (!PublicUrlGuard.TryParse(url, out var uri))
            return Failure("invalid_url");

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(RequestTimeout);
        var token = timeout.Token;

        try
        {
            for (var redirects = 0; ; redirects++)
            {
                // Check each hop before sending. The production ConnectCallback resolves again
                // and connects directly to a checked IP to close the DNS rebinding gap.
                await guard.ResolvePublicAddressesAsync(uri!.Host, token);
                using var request = new HttpRequestMessage(HttpMethod.Get, uri);
                using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);

                if (IsRedirect(response.StatusCode))
                {
                    if (redirects == MaxRedirects) return Failure("too_many_redirects", response.StatusCode);
                    if (response.Headers.Location is null) return Failure("invalid_redirect", response.StatusCode);
                    if (!Uri.TryCreate(uri, response.Headers.Location, out var next))
                        return Failure("invalid_redirect", response.StatusCode);
                    if (!PublicUrlGuard.TryParse(next.AbsoluteUri, out uri))
                        return Failure("unsafe_redirect", response.StatusCode);
                    continue;
                }

                if (!response.IsSuccessStatusCode) return Failure("http_error", response.StatusCode);
                var mediaType = response.Content.Headers.ContentType?.MediaType;
                if (mediaType is not ("text/html" or "application/xhtml+xml"))
                    return Failure("invalid_content_type", response.StatusCode);
                if (response.Content.Headers.ContentLength > MaxResponseBytes)
                    return Failure("response_too_large", response.StatusCode);

                await using var stream = await response.Content.ReadAsStreamAsync(token);
                using var bytes = new MemoryStream();
                var buffer = new byte[8192];
                while (true)
                {
                    var read = await stream.ReadAsync(buffer, token);
                    if (read == 0) break;
                    if (bytes.Length + read > MaxResponseBytes)
                        return Failure("response_too_large", response.StatusCode);
                    bytes.Write(buffer, 0, read);
                }

                Encoding encoding;
                try
                {
                    var charset = response.Content.Headers.ContentType?.CharSet?.Trim('"');
                    encoding = charset is null ? Encoding.UTF8 : Encoding.GetEncoding(charset);
                }
                catch (ArgumentException)
                {
                    return Failure("invalid_charset", response.StatusCode);
                }
                return new PageFetchResult(encoding.GetString(bytes.ToArray()), uri, null, response.StatusCode);
            }
        }
        catch (UnsafeDestinationException) { return Failure("unsafe_destination"); }
        catch (SocketException ex)
        {
            if (cancellationToken.IsCancellationRequested) throw new OperationCanceledException(cancellationToken);
            if (timeout.IsCancellationRequested) return Failure("timeout");
            return Failure(IsDnsError(ex) ? "dns_error" : "connection_error");
        }
        catch (HttpRequestException ex)
        {
            if (cancellationToken.IsCancellationRequested) throw new OperationCanceledException(cancellationToken);
            if (timeout.IsCancellationRequested) return Failure("timeout");
            if (FindInner<UnsafeDestinationException>(ex) is not null)
                return Failure("unsafe_destination");
            var socket = FindInner<SocketException>(ex);
            return Failure(socket is not null && IsDnsError(socket) ? "dns_error" : "connection_error");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Failure("timeout");
        }
        catch (IOException) { return Failure("connection_error"); }
    }

    private static bool IsRedirect(HttpStatusCode status) => status is
        HttpStatusCode.MovedPermanently or HttpStatusCode.Redirect or HttpStatusCode.SeeOther or
        HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect;

    private static bool IsDnsError(SocketException error) => error.SocketErrorCode is
        SocketError.HostNotFound or SocketError.TryAgain or SocketError.NoData;

    private static T? FindInner<T>(Exception error) where T : Exception
    {
        for (Exception? current = error; current is not null; current = current.InnerException)
            if (current is T found) return found;
        return null;
    }

    private static PageFetchResult Failure(string code, HttpStatusCode? status = null) =>
        new(null, null, code, status);
}
