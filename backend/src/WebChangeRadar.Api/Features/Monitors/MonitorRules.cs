using WebChangeRadar.Api.Services.Fetching;
using WebChangeRadar.Api.Services.Extraction;
using System.Text.Json;

namespace WebChangeRadar.Api.Features.Monitors;

public static class MonitorRules
{
    public const int MaxPerUser = 3;
    public const int MaxPageSize = 100;

    public static Dictionary<string, string[]> Validate(string? name, string? url, int intervalMinutes,
        string? contentSelector = null, string[]? keywords = null)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 120 || name.Any(char.IsControl))
            errors["name"] = ["Name must contain 1 to 120 characters."];

        if (!PublicUrlGuard.TryParse(url, out _))
            errors["url"] = ["Enter a public absolute HTTP or HTTPS URL without credentials (at most 2048 characters)."];

        if (intervalMinutes is not (60 or 360 or 1440))
            errors["intervalMinutes"] = ["Allowed values are 60, 360, or 1440 minutes."];
        if (!string.IsNullOrWhiteSpace(contentSelector) &&
            (contentSelector.Trim().Length > 160 || !ContentExtractor.IsValidSelector(contentSelector.Trim())))
            errors["contentSelector"] = ["Enter a valid CSS selector of at most 160 characters."];
        if (keywords is { Length: > 10 } || keywords?.Any(x => string.IsNullOrWhiteSpace(x) || x.Trim().Length > 60) == true)
            errors["keywords"] = ["Use at most 10 non-empty phrases of at most 60 characters each."];
        return errors;
    }

    public static string[] NormalizeKeywords(string[]? keywords) =>
        (keywords ?? []).Select(x => x.Trim().Normalize(System.Text.NormalizationForm.FormC))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

    public static string SerializeKeywords(string[]? keywords) => JsonSerializer.Serialize(NormalizeKeywords(keywords));
    public static string[] ReadKeywords(string json) => JsonSerializer.Deserialize<string[]>(json) ?? [];
    public static bool ShouldNotify(string text, string keywordsJson)
    {
        var keywords = ReadKeywords(keywordsJson);
        return keywords.Length == 0 ||
            keywords.Any(x => text.Contains(x, StringComparison.OrdinalIgnoreCase));
    }

    public static DateTime NextCheckAt(DateTime now, int intervalMinutes) => now.AddMinutes(intervalMinutes);

}
