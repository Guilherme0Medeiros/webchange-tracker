namespace WebChangeRadar.Api.Data;

public sealed class EmailOutbox
{
    public Guid Id { get; set; }
    public Guid ChangeEventId { get; set; }
    public ChangeEvent ChangeEvent { get; set; } = null!;
    public required string UserId { get; set; }
    public ApplicationUser User { get; set; } = null!;
    public required string Recipient { get; set; }
    public string Status { get; set; } = "pending";
    public int Attempts { get; set; }
    public DateTime NextAttemptAt { get; set; }
    public Guid? LeaseToken { get; set; }
    public DateTime? LeaseExpiresAt { get; set; }
    public DateTime? SentAt { get; set; }
    public string? LastErrorCode { get; set; }
}
