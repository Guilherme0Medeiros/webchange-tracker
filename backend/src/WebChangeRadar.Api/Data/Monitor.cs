namespace WebChangeRadar.Api.Data;

public sealed class Monitor
{
    public Guid Id { get; set; }
    public required string UserId { get; set; }
    public ApplicationUser User { get; set; } = null!;
    public required string Name { get; set; }
    public required string Url { get; set; }
    public string? ContentSelector { get; set; }
    public string KeywordsJson { get; set; } = "[]";
    public bool EmailAlertsEnabled { get; set; }
    public int IntervalMinutes { get; set; }
    public string Status { get; set; } = "active";
    public DateTime? NextCheckAt { get; set; }
    public DateTime? BaselineResetAt { get; set; }
    public Guid? CurrentSnapshotId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
