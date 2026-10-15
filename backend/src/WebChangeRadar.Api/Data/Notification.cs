namespace WebChangeRadar.Api.Data;

public sealed class Notification
{
    public Guid Id { get; set; }
    public required string UserId { get; set; }
    public ApplicationUser User { get; set; } = null!;
    public Guid ChangeEventId { get; set; }
    public ChangeEvent ChangeEvent { get; set; } = null!;
    public DateTime CreatedAt { get; set; }
    public DateTime? ReadAt { get; set; }
}
