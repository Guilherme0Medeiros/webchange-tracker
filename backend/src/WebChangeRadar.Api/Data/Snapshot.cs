namespace WebChangeRadar.Api.Data;

public sealed class Snapshot
{
    public Guid Id { get; set; }
    public Guid MonitorId { get; set; }
    public Monitor Monitor { get; set; } = null!;
    public required string ContentHash { get; set; }
    public required string NormalizedText { get; set; }
    public DateTime CapturedAt { get; set; }
}
