namespace WebChangeRadar.Api.Data;

public sealed class CheckRun
{
    public Guid Id { get; set; }
    public Guid MonitorId { get; set; }
    public Monitor Monitor { get; set; } = null!;
    public string TriggerType { get; set; } = "scheduled";
    public string Status { get; set; } = "pending";
    public DateTime RequestedAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? FinishedAt { get; set; }
    public int? HttpStatusCode { get; set; }
    public string? ErrorCode { get; set; }
    public Guid? LeaseToken { get; set; }
    public DateTime? LeaseExpiresAt { get; set; }
}
