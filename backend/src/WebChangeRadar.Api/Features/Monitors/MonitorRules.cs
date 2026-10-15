using WebChangeRadar.Api.Services.Fetching;

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

        if (!PublicUrlGuard.TryParse(url, out _))
            errors["url"] = ["Enter a public absolute HTTP or HTTPS URL without credentials (at most 2048 characters)."];

        if (intervalMinutes is not (60 or 360 or 1440))
            errors["intervalMinutes"] = ["Allowed values are 60, 360, or 1440 minutes."];
        return errors;
    }

    public static DateTime NextCheckAt(DateTime now, int intervalMinutes) => now.AddMinutes(intervalMinutes);

}
