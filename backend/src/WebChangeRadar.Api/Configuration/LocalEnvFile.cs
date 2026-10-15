namespace WebChangeRadar.Api.Configuration;

public static class LocalEnvFile
{
    public static string? GetValue(string key)
    {
        var environmentValue = Environment.GetEnvironmentVariable(key);
        if (environmentValue is not null) return environmentValue;
        return Read().TryGetValue(key, out var fileValue) ? fileValue : null;
    }

    public static IReadOnlyDictionary<string, string?> Read()
    {
        var root = FindRoot(Directory.GetCurrentDirectory()) ?? FindRoot(AppContext.BaseDirectory);
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        if (root is null) return values;

        var path = Path.Combine(root, ".env");
        if (!File.Exists(path)) return values;
        foreach (var line in File.ReadLines(path))
        {
            var trimmed = line.Trim().TrimStart('\uFEFF');
            if (trimmed.Length == 0 || trimmed.StartsWith('#')) continue;
            var separator = trimmed.IndexOf('=');
            if (separator <= 0) continue;
            var key = trimmed[..separator].Trim();
            if (!IsValidKey(key)) continue;
            var value = trimmed[(separator + 1)..].Trim();
            if (value.Length >= 2 && value[0] == value[^1] && value[0] is '\'' or '"')
                value = value[1..^1];
            values[key] = value;
        }
        return values;
    }

    private static string? FindRoot(string start)
    {
        for (var directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "WebChangeRadar.slnx")))
                return directory.FullName;
        return null;
    }

    private static bool IsValidKey(string key) => key.Length > 0 &&
        (char.IsLetter(key[0]) || key[0] == '_') &&
        key.Skip(1).All(character => char.IsLetterOrDigit(character) || character == '_');
}
