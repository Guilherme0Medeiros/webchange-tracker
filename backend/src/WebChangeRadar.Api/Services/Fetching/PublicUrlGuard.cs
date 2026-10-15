using System.Net;
using System.Net.Sockets;

namespace WebChangeRadar.Api.Services.Fetching;

public interface IHostAddressResolver
{
    Task<IPAddress[]> ResolveAsync(string host, CancellationToken cancellationToken);
}

public sealed class SystemHostAddressResolver : IHostAddressResolver
{
    public Task<IPAddress[]> ResolveAsync(string host, CancellationToken cancellationToken) =>
        Dns.GetHostAddressesAsync(host, cancellationToken);
}

public sealed class UnsafeDestinationException : Exception
{
    public UnsafeDestinationException() : base("The URL does not point to a public destination.") { }
}

public sealed class PublicUrlGuard(IHostAddressResolver resolver)
{
    public static bool TryParse(string? url, out Uri? uri)
    {
        uri = null;
        if (string.IsNullOrWhiteSpace(url) || url.Length > 2048 ||
            !Uri.TryCreate(url, UriKind.Absolute, out var parsed) ||
            (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps) ||
            string.IsNullOrEmpty(parsed.Host) || !string.IsNullOrEmpty(parsed.UserInfo) ||
            !IsPublicHostName(parsed.Host)) return false;

        uri = parsed;
        return true;
    }

    public async Task<IPAddress[]> ResolvePublicAddressesAsync(string host, CancellationToken cancellationToken)
    {
        if (!IsPublicHostName(host)) throw new UnsafeDestinationException();
        var addresses = IPAddress.TryParse(host, out var literal)
            ? [literal]
            : await resolver.ResolveAsync(host, cancellationToken);
        if (addresses.Length == 0)
            throw new SocketException((int)SocketError.HostNotFound);
        // Reject the whole DNS answer if any address is unsafe. The socket uses only these checked IPs.
        if (addresses.Any(address => !IsPublicAddress(address)))
            throw new UnsafeDestinationException();
        return addresses;
    }

    private static bool IsPublicHostName(string host)
    {
        var normalized = host.TrimEnd('.');
        if (IPAddress.TryParse(normalized, out var literal)) return IsPublicAddress(literal);
        if (!normalized.Contains('.') || normalized.StartsWith('.') ||
            normalized.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase) ||
            normalized.EndsWith(".local", StringComparison.OrdinalIgnoreCase) ||
            normalized.EndsWith(".internal", StringComparison.OrdinalIgnoreCase) ||
            normalized.EndsWith(".home.arpa", StringComparison.OrdinalIgnoreCase) ||
            normalized.EndsWith(".test", StringComparison.OrdinalIgnoreCase) ||
            normalized.EndsWith(".invalid", StringComparison.OrdinalIgnoreCase)) return false;
        return true;
    }

    public static bool IsPublicAddress(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        var b = address.GetAddressBytes();
        if (b.Length == 4)
            return b[0] is >= 1 and <= 223 &&
                   b[0] is not (10 or 127) &&
                   !(b[0] == 100 && b[1] is >= 64 and <= 127) &&
                   !(b[0] == 169 && b[1] == 254) &&
                   !(b[0] == 172 && b[1] is >= 16 and <= 31) &&
                   !(b[0] == 192 && (b[1] == 168 || b[1] == 0 ||
                     (b[1] == 88 && b[2] == 99))) &&
                   !(b[0] == 198 && (b[1] is 18 or 19 || (b[1] == 51 && b[2] == 100))) &&
                   !(b[0] == 203 && b[1] == 0 && b[2] == 113);

        // Only global IPv6 unicast; exclude documentation and transition ranges that can
        // carry embedded IPv4 destinations (6to4/Teredo).
        return b.Length == 16 && (b[0] & 0xe0) == 0x20 &&
               !(b[0] == 0x20 && b[1] == 0x01 &&
                 ((b[2] == 0x0d && b[3] == 0xb8) || (b[2] == 0 && b[3] == 0))) &&
               !(b[0] == 0x20 && b[1] == 0x02) &&
               !(b[0] == 0x3f && b[1] == 0xff);
    }
}
