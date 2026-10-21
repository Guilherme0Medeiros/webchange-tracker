using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
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
        group.MapGet("/email-status", EmailStatusAsync);
        group.MapPost("/request-email-confirmation", RequestConfirmationAsync)
            .AddEndpointFilter<RequireCsrfTokenFilter>().RequireRateLimiting("auth");
        app.MapGet("/api/notifications/confirm-email", ConfirmEmailAsync);
    }

    private static async Task<IResult> ListAsync(HttpContext http, ApplicationDbContext db,
        int page = 1, int pageSize = 20, bool? unread = null, CancellationToken ct = default)
    {
        if (page < 1 || pageSize is < 1 or > 100 || page > int.MaxValue / pageSize)
            return Results.Problem(statusCode: 400, title: "Paginação inválida.");
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
        if (notification is null) return Results.Problem(statusCode: 404, title: "Notificação não encontrada.");
        notification.ReadAt ??= clock.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(ct);
        return Results.Ok(new { notification.Id, notification.ReadAt });
    }

    private static async Task<IResult> EmailStatusAsync(HttpContext http, ApplicationDbContext db,
        IChangeEmailSender sender, CancellationToken ct)
    {
        var userId = http.User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var confirmed = await db.Users.Where(x => x.Id == userId).Select(x => x.EmailConfirmed)
            .SingleAsync(ct);
        return Results.Ok(new { available = sender.IsConfigured, confirmed,
            pending = await db.EmailOutbox.CountAsync(x => x.UserId == userId && x.Status == "pending", ct),
            failed = await db.EmailOutbox.CountAsync(x => x.UserId == userId && x.Status == "failed", ct) });
    }

    private static async Task<IResult> RequestConfirmationAsync(HttpContext http,
        UserManager<ApplicationUser> users, IChangeEmailSender sender, IConfiguration configuration,
        CancellationToken ct)
    {
        if (!sender.IsConfigured) return Results.Problem(statusCode: 503, title: "O envio de e-mails está indisponível no momento.");
        var user = await users.FindByIdAsync(http.User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        if (user is null) return Results.Unauthorized();
        if (user.EmailConfirmed) return Results.NoContent();
        var token = await users.GenerateEmailConfirmationTokenAsync(user);
        var baseUri = new Uri(configuration["Email:PublicBaseUrl"]!);
        var link = new Uri(baseUri, "/api/notifications/confirm-email?userId=" +
            Uri.EscapeDataString(user.Id) + "&token=" + Uri.EscapeDataString(token)).ToString();
        try { await sender.SendConfirmationAsync(user.Email!, link, ct); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch { return Results.Problem(statusCode: 503, title: "Não foi possível enviar o e-mail agora. Tente novamente mais tarde."); }
        return Results.Accepted();
    }

    private static async Task<IResult> ConfirmEmailAsync(UserManager<ApplicationUser> users,
        string? userId, string? token)
    {
        if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(token))
            return Results.BadRequest();
        var user = await users.FindByIdAsync(userId);
        if (user is null) return Results.BadRequest();
        var result = await users.ConfirmEmailAsync(user, token);
        return result.Succeeded ? Results.Redirect("/dashboard") : Results.BadRequest();
    }
}
