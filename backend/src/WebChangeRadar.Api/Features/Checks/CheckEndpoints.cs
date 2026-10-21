using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using WebChangeRadar.Api.Data;

namespace WebChangeRadar.Api.Features.Checks;

public static class CheckEndpoints
{
    public static void MapCheckEndpoints(this IEndpointRouteBuilder app)
    {
        var monitors = app.MapGroup("/api/monitors/{id:guid}").RequireAuthorization();
        monitors.MapGet("/checks", ListChecksAsync);
        monitors.MapGet("/changes", ListChangesAsync);
        app.MapGet("/api/changes/{id:guid}", GetChangeAsync).RequireAuthorization();
        app.MapGet("/api/dashboard", DashboardAsync).RequireAuthorization();
    }

    private static async Task<IResult> ListChecksAsync(HttpContext http, ApplicationDbContext db,
        Guid id, int page = 1, int pageSize = 20, string? result = null,
        DateTime? from = null, DateTime? to = null, CancellationToken ct = default)
    {
        if (!ValidPage(page, pageSize) || result is not null && result is not ("pending" or "running" or "success" or "failed" or "stale") || !ValidPeriod(from, to))
            return BadQuery();
        if (!await Owns(db, id, UserId(http), ct)) return Missing();
        var query = db.CheckRuns.AsNoTracking().Where(x => x.MonitorId == id && x.Monitor.UserId == UserId(http));
        if (result is not null) query = query.Where(x => x.Status == result);
        if (from is not null) query = query.Where(x => x.RequestedAt >= from);
        if (to is not null) query = query.Where(x => x.RequestedAt <= to);
        var count = await query.CountAsync(ct);
        var items = await query.OrderByDescending(x => x.RequestedAt).ThenBy(x => x.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new { x.Id, x.TriggerType, x.Status, x.RequestedAt, x.StartedAt,
                x.FinishedAt, x.HttpStatusCode, x.ErrorCode,
                DurationMs = x.StartedAt != null && x.FinishedAt != null ?
                    (double?)(x.FinishedAt.Value - x.StartedAt.Value).TotalMilliseconds : null })
            .ToListAsync(ct);
        return Results.Ok(new { items, page, pageSize, totalCount = count });
    }

    private static async Task<IResult> ListChangesAsync(HttpContext http, ApplicationDbContext db,
        Guid id, int page = 1, int pageSize = 20, DateTime? from = null, DateTime? to = null,
        CancellationToken ct = default)
    {
        if (!ValidPage(page, pageSize) || !ValidPeriod(from, to)) return BadQuery();
        if (!await Owns(db, id, UserId(http), ct)) return Missing();
        var query = db.ChangeEvents.AsNoTracking().Where(x => x.MonitorId == id && x.Monitor.UserId == UserId(http));
        if (from is not null) query = query.Where(x => x.DetectedAt >= from);
        if (to is not null) query = query.Where(x => x.DetectedAt <= to);
        var count = await query.CountAsync(ct);
        var items = await query.OrderByDescending(x => x.DetectedAt).ThenBy(x => x.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new { x.Id, x.MonitorId, x.OldSnapshotId, x.NewSnapshotId, x.DetectedAt })
            .ToListAsync(ct);
        return Results.Ok(new { items, page, pageSize, totalCount = count });
    }

    private static async Task<IResult> GetChangeAsync(HttpContext http, ApplicationDbContext db,
        Guid id, CancellationToken ct)
    {
        var change = await db.ChangeEvents.AsNoTracking()
            .Where(x => x.Id == id && x.Monitor.UserId == UserId(http))
            .Select(x => new { x.Id, x.MonitorId, x.DetectedAt,
                OldText = x.OldSnapshot.NormalizedText, NewText = x.NewSnapshot.NormalizedText })
            .SingleOrDefaultAsync(ct);
        if (change is null) return Missing();
        var oldLines = change.OldText.Split('\n');
        var newLines = change.NewText.Split('\n');
        var prefix = 0;
        while (prefix < oldLines.Length && prefix < newLines.Length && oldLines[prefix] == newLines[prefix]) prefix++;
        var suffix = 0;
        while (suffix < oldLines.Length - prefix && suffix < newLines.Length - prefix &&
            oldLines[^(suffix + 1)] == newLines[^(suffix + 1)]) suffix++;
        return Results.Ok(new { change.Id, change.MonitorId, change.DetectedAt,
            oldText = change.OldText, newText = change.NewText,
            removed = oldLines.Skip(prefix).Take(oldLines.Length - prefix - suffix),
            added = newLines.Skip(prefix).Take(newLines.Length - prefix - suffix) });
    }

    private static async Task<IResult> DashboardAsync(HttpContext http, ApplicationDbContext db, CancellationToken ct)
    {
        var userId = UserId(http);
        var monitors = db.Monitors.Where(x => x.UserId == userId);
        return Results.Ok(new
        {
            activeMonitors = await monitors.CountAsync(x => x.Status == "active", ct),
            checks = await db.CheckRuns.CountAsync(x => x.Monitor.UserId == userId, ct),
            changes = await db.ChangeEvents.CountAsync(x => x.Monitor.UserId == userId, ct),
            failures = await db.CheckRuns.CountAsync(x => x.Monitor.UserId == userId && x.Status == "failed", ct)
        });
    }

    private static string UserId(HttpContext http) => http.User.FindFirstValue(ClaimTypes.NameIdentifier)!;
    private static Task<bool> Owns(ApplicationDbContext db, Guid id, string userId, CancellationToken ct) =>
        db.Monitors.AnyAsync(x => x.Id == id && x.UserId == userId, ct);
    private static bool ValidPage(int page, int size) => page >= 1 && size is >= 1 and <= 100 && page <= int.MaxValue / size;
    private static bool ValidPeriod(DateTime? from, DateTime? to) =>
        (from is null || from.Value.Kind == DateTimeKind.Utc) &&
        (to is null || to.Value.Kind == DateTimeKind.Utc) &&
        (from is null || to is null || from <= to);
    private static IResult Missing() => Results.Problem(statusCode: 404, title: "Recurso não encontrado.");
    private static IResult BadQuery() => Results.Problem(statusCode: 400, title: "Filtros de consulta inválidos.");
}
