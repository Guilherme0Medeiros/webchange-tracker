using Microsoft.AspNetCore.Identity;

namespace WebChangeRadar.Api.Data;

public sealed class ApplicationUser : IdentityUser
{
    public ICollection<Monitor> Monitors { get; } = new List<Monitor>();
}
