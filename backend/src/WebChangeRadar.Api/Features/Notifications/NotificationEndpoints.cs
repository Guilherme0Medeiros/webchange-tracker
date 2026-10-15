using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using WebChangeRadar.Api.Data;
using WebChangeRadar.Api.Features.Auth;

namespace WebChangeRadar.Api.Features.Notifications;

public static class NotificationEndpoints
{
    public static void MapNotificationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/notifications").RequireAuthorization();
        group.MapGet("", ListAsync);
        group.MapPatch("/{id:guid}/read", ReadAsync).AddEndpointFilter<RequireCsrfTokenFilter>();
    }

    private static async Task<IResult> ListAsync(HttpContext http, ApplicationDbContext db,
        int page = 1, int pageSize = 20, bool? unread = null, CancellationToken ct = default)
    {
        if (page < 1 || pageSize is < 1 or > 100 || page > int.MaxValue / pageSize)
            return Results.Problem(statusCode: 400, title: "Invalid pagination.");
        var userId = http.User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var query = db.Notifications.AsNoTracking().Where(x => x.UserId == userId);
        if (unread is not null) query = query.Where(x => (x.ReadAt == null) == unread.Value);
        var count = await query.CountAsync(ct);
        var items = await query.OrderByDescending(x => x.CreatedAt).ThenBy(x => x.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new { x.Id, x.ChangeEventId, x.ChangeEvent.MonitorId, x.CreatedAt, x.ReadAt })
            .ToListAsync(ct);
        return Results.Ok(new { items, page, pageSize, totalCount = count });
    }

    private static async Task<IResult> ReadAsync(HttpContext http, ApplicationDbContext db,
        TimeProvider clock, Guid id, CancellationToken ct)
    {
        var userId = http.User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var notification = await db.Notifications.SingleOrDefaultAsync(x => x.Id == id && x.UserId == userId, ct);
        if (notification is null) return Results.Problem(statusCode: 404, title: "Notification not found.");
        notification.ReadAt ??= clock.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(ct);
        return Results.Ok(new { notification.Id, notification.ReadAt });
    }
}
