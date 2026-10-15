using System.Net;

namespace WebChangeRadar.Api.Features.Monitors;

public static class MonitorRules
{
    public const int MaxPerUser = 3;
    public const int MaxPageSize = 100;

    public static Dictionary<string, string[]> Validate(string? name, string? url, int intervalMinutes)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 120)
            errors["name"] = ["Name must contain 1 to 120 characters."];

        if (string.IsNullOrWhiteSpace(url) || url.Length > 2048 ||
            !Uri.TryCreate(url, UriKind.Absolute, out var parsed) ||
            (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps) ||
            string.IsNullOrEmpty(parsed.Host) || !string.IsNullOrEmpty(parsed.UserInfo) ||
            IsLocalDestination(parsed.Host))
            errors["url"] = ["Enter a public absolute HTTP or HTTPS URL without credentials (at most 2048 characters)."];

        if (intervalMinutes is not (60 or 360 or 1440))
            errors["intervalMinutes"] = ["Allowed values are 60, 360, or 1440 minutes."];
        return errors;
    }

    public static DateTime NextCheckAt(DateTime now, int intervalMinutes) => now.AddMinutes(intervalMinutes);

    private static bool IsLocalDestination(string host)
    {
        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase)) return true;
        if (!IPAddress.TryParse(host, out var address)) return false;
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) ||
            address.Equals(IPAddress.IPv6Any)) return true;
        var bytes = address.GetAddressBytes();
        if (bytes.Length == 16)
            return (bytes[0] & 0xfe) == 0xfc || (bytes[0] == 0xfe && (bytes[1] & 0xc0) == 0x80);
        return bytes[0] is 0 or 10 or 127 or >= 224 ||
            (bytes[0] == 169 && bytes[1] == 254) ||
            (bytes[0] == 172 && bytes[1] is >= 16 and <= 31) ||
            (bytes[0] == 192 && bytes[1] == 168) ||
            (bytes[0] == 100 && bytes[1] is >= 64 and <= 127);
    }
}
