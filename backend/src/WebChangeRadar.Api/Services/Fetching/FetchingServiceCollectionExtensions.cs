using System.Net.Sockets;

namespace WebChangeRadar.Api.Services.Fetching;

public static class FetchingServiceCollectionExtensions
{
    public static IServiceCollection AddPageFetching(this IServiceCollection services)
    {
        services.AddSingleton<IHostAddressResolver, SystemHostAddressResolver>();
        services.AddSingleton<PublicUrlGuard>();
        services.AddHttpClient<IPageFetcher, PageFetcher>(client =>
        {
            client.Timeout = Timeout.InfiniteTimeSpan; // PageFetcher limits the complete redirect chain.
            client.DefaultRequestHeaders.UserAgent.ParseAdd("WebChangeRadar/1.0");
        })
        .ConfigurePrimaryHttpMessageHandler(provider =>
        {
            var guard = provider.GetRequiredService<PublicUrlGuard>();
            return new SocketsHttpHandler
            {
                AllowAutoRedirect = false,
                UseCookies = false,
                UseProxy = false,
                PooledConnectionLifetime = TimeSpan.FromMinutes(2),
                ConnectCallback = async (context, cancellationToken) =>
                {
                    var addresses = await guard.ResolvePublicAddressesAsync(
                        context.DnsEndPoint.Host, cancellationToken);
                    SocketException? lastError = null;
                    foreach (var address in addresses)
                    {
                        var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                        try
                        {
                            await socket.ConnectAsync(address, context.DnsEndPoint.Port, cancellationToken);
                            return new NetworkStream(socket, ownsSocket: true);
                        }
                        catch (SocketException error)
                        {
                            lastError = error;
                            socket.Dispose();
                        }
                        catch
                        {
                            socket.Dispose();
                            throw;
                        }
                    }
                    throw lastError ?? new SocketException((int)SocketError.HostUnreachable);
                }
            };
        });
        return services;
    }
}
