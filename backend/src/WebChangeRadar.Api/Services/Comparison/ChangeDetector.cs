using System.Security.Cryptography;
using System.Text;

namespace WebChangeRadar.Api.Services.Comparison;

public enum ContentChange { Baseline, Unchanged, Changed }

public sealed record ChangeDetection(string ContentHash, ContentChange Kind);

public static class ChangeDetector
{
    public static ChangeDetection Detect(string normalizedText, string? previousHash)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalizedText)));
        var kind = previousHash is null ? ContentChange.Baseline :
            previousHash == hash ? ContentChange.Unchanged : ContentChange.Changed;
        return new ChangeDetection(hash, kind);
    }
}
