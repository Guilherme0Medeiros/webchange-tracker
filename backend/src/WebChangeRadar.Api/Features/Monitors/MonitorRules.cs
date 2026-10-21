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
            errors["name"] = ["Informe um nome de 1 a 120 caracteres, sem caracteres de controle."];

        if (!PublicUrlGuard.TryParse(url, out _))
            errors["url"] = ["Informe uma URL pública HTTP ou HTTPS, sem usuário ou senha, com até 2.048 caracteres."];

        if (intervalMinutes is not (60 or 360 or 1440))
            errors["intervalMinutes"] = ["Escolha um intervalo de 60, 360 ou 1.440 minutos."];
        if (!string.IsNullOrWhiteSpace(contentSelector) &&
            (contentSelector.Trim().Length > 160 || !ContentExtractor.IsValidSelector(contentSelector.Trim())))
            errors["contentSelector"] = ["Informe um seletor CSS válido com até 160 caracteres."];
        if (keywords is { Length: > 10 } || keywords?.Any(x => string.IsNullOrWhiteSpace(x) || x.Trim().Length > 60) == true)
            errors["keywords"] = ["Informe até 10 expressões não vazias, com até 60 caracteres cada."];
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
