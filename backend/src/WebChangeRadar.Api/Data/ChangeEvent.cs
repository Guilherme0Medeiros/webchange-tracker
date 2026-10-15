namespace WebChangeRadar.Api.Data;

public sealed class ChangeEvent
{
    public Guid Id { get; set; }
    public Guid MonitorId { get; set; }
    public Monitor Monitor { get; set; } = null!;
    public Guid OldSnapshotId { get; set; }
    public Snapshot OldSnapshot { get; set; } = null!;
    public Guid NewSnapshotId { get; set; }
    public Snapshot NewSnapshot { get; set; } = null!;
    public DateTime DetectedAt { get; set; }
}
