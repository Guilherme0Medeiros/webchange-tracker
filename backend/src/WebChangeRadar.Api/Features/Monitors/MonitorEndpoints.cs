using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using WebChangeRadar.Api.Data;
using WebChangeRadar.Api.Features.Auth;
using Monitor = WebChangeRadar.Api.Data.Monitor;

namespace WebChangeRadar.Api.Features.Monitors;

public static class MonitorEndpoints
{
    public static void MapMonitorEndpoints(this IEndpointRouteBuilder app)
    {
        var monitors = app.MapGroup("/api/monitors").RequireAuthorization();
        monitors.MapGet("", ListAsync);
        monitors.MapPost("", CreateAsync).AddEndpointFilter<RequireCsrfTokenFilter>();
        monitors.MapGet("/{id:guid}", GetAsync);
        monitors.MapPut("/{id:guid}", UpdateAsync).AddEndpointFilter<RequireCsrfTokenFilter>();
        monitors.MapPatch("/{id:guid}/status", SetStatusAsync).AddEndpointFilter<RequireCsrfTokenFilter>();
        monitors.MapDelete("/{id:guid}", DeleteAsync).AddEndpointFilter<RequireCsrfTokenFilter>();
    }

    private static async Task<IResult> ListAsync(
        HttpContext http, ApplicationDbContext db, CheckSchedule schedule, string? status, int page = 1, int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var errors = new Dictionary<string, string[]>();
        if (status is not null && status is not ("active" or "paused"))
            errors["status"] = ["O status deve ser ativo ou pausado."];
        if (page < 1) errors["page"] = ["A página deve ser maior que zero."];
        if (pageSize is < 1 or > MonitorRules.MaxPageSize)
            errors["pageSize"] = [$"O tamanho da página deve estar entre 1 e {MonitorRules.MaxPageSize}."];
        if (pageSize > 0 && page > int.MaxValue / pageSize)
            errors["page"] = ["O número da página é muito alto."];
        if (errors.Count > 0) return Results.ValidationProblem(errors, title: "Uma ou mais informações são inválidas.");

        var userId = UserId(http);
        var query = db.Monitors.AsNoTracking().Where(m => m.UserId == userId);
        if (status is not null) query = query.Where(m => m.Status == status);
        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query.OrderByDescending(m => m.CreatedAt).ThenBy(m => m.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(m => new MonitorResponse(m.Id, m.Name, m.Url, m.IntervalMinutes,
                m.Status, m.NextCheckAt, m.CreatedAt, m.UpdatedAt,
                db.CheckRuns.Where(c => c.MonitorId == m.Id).Max(c => c.FinishedAt),
                m.ContentSelector, m.KeywordsJson, null, m.EmailAlertsEnabled))
            .ToListAsync(cancellationToken);
        return Results.Ok(new MonitorPageResponse(items.Select(m => m with
        { NextCheckAt = schedule.NextCheckAt(m.NextCheckAt, m.IntervalMinutes),
          Keywords = MonitorRules.ReadKeywords(m.KeywordsJson!) }).ToList(), page, pageSize, totalCount));
    }

    private static async Task<IResult> CreateAsync(
        HttpContext http, ApplicationDbContext db, MonitorRequest request, TimeProvider clock, CheckSchedule schedule,
        CancellationToken cancellationToken)
    {
        var errors = MonitorRules.Validate(request.Name, request.Url, request.IntervalMinutes,
            request.ContentSelector, request.Keywords);
        if (errors.Count > 0) return Results.ValidationProblem(errors, title: "Uma ou mais informações são inválidas.");

        var userId = UserId(http);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        // Serialize creations for this user so simultaneous requests cannot exceed the limit.
        await db.Users.FromSqlInterpolated($"SELECT * FROM \"AspNetUsers\" WHERE \"Id\" = {userId} FOR UPDATE")
            .AsNoTracking().SingleAsync(cancellationToken);
        if (await db.Monitors.CountAsync(m => m.UserId == userId, cancellationToken) >= MonitorRules.MaxPerUser)
            return Results.Problem(statusCode: 409, title: "Limite de monitoramentos atingido.",
                detail: $"Você pode ter até {MonitorRules.MaxPerUser} monitoramentos.");

        var now = clock.GetUtcNow().UtcDateTime;
        var monitor = new Monitor
        {
            Id = Guid.NewGuid(), UserId = userId, Name = request.Name!.Trim(), Url = request.Url!.Trim(),
            ContentSelector = request.ContentSelector?.Trim() is { Length: > 0 } selector ? selector : null,
            KeywordsJson = MonitorRules.SerializeKeywords(request.Keywords),
            EmailAlertsEnabled = request.EmailAlertsEnabled ?? false,
            IntervalMinutes = request.IntervalMinutes, Status = "active",
            NextCheckAt = now, CreatedAt = now, UpdatedAt = now
        };
        db.Monitors.Add(monitor);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Results.Created($"/api/monitors/{monitor.Id}", ToResponse(monitor, schedule));
    }

    private static async Task<IResult> GetAsync(
        HttpContext http, ApplicationDbContext db, CheckSchedule schedule, Guid id, CancellationToken cancellationToken)
    {
        var monitor = await db.Monitors.AsNoTracking()
            .Where(m => m.Id == id && m.UserId == UserId(http))
            .Select(m => new MonitorResponse(m.Id, m.Name, m.Url, m.IntervalMinutes,
                m.Status, m.NextCheckAt, m.CreatedAt, m.UpdatedAt,
                db.CheckRuns.Where(c => c.MonitorId == m.Id).Max(c => c.FinishedAt),
                m.ContentSelector, m.KeywordsJson, null, m.EmailAlertsEnabled))
            .SingleOrDefaultAsync(cancellationToken);
        return monitor is null ? NotFound() : Results.Ok(monitor with
        { NextCheckAt = schedule.NextCheckAt(monitor.NextCheckAt, monitor.IntervalMinutes),
          Keywords = MonitorRules.ReadKeywords(monitor.KeywordsJson!) });
    }

    private static async Task<IResult> UpdateAsync(
        HttpContext http, ApplicationDbContext db, Guid id, MonitorRequest request, TimeProvider clock, CheckSchedule schedule,
        CancellationToken cancellationToken)
    {
        var errors = MonitorRules.Validate(request.Name, request.Url, request.IntervalMinutes,
            request.ContentSelector, request.Keywords);
        if (errors.Count > 0) return Results.ValidationProblem(errors, title: "Uma ou mais informações são inválidas.");
        var monitor = await OwnedMonitorAsync(db, UserId(http), id, cancellationToken);
        if (monitor is null) return NotFound();

        var now = clock.GetUtcNow().UtcDateTime;
        var urlChanged = monitor.Url != request.Url!.Trim();
        var selector = request.ContentSelector?.Trim() is { Length: > 0 } selected ? selected : null;
        var selectorChanged = monitor.ContentSelector != selector;
        var intervalChanged = monitor.IntervalMinutes != request.IntervalMinutes;
        monitor.Name = request.Name!.Trim();
        monitor.Url = request.Url.Trim();
        monitor.ContentSelector = selector;
        monitor.KeywordsJson = MonitorRules.SerializeKeywords(request.Keywords);
        monitor.EmailAlertsEnabled = request.EmailAlertsEnabled ?? monitor.EmailAlertsEnabled;
        monitor.IntervalMinutes = request.IntervalMinutes;
        if (urlChanged || selectorChanged)
        {
            monitor.BaselineResetAt = now;
            monitor.CurrentSnapshotId = null;
        }
        if (monitor.Status == "active" && (urlChanged || selectorChanged || intervalChanged))
            monitor.NextCheckAt = urlChanged || selectorChanged ? now : MonitorRules.NextCheckAt(now, request.IntervalMinutes);
        monitor.UpdatedAt = now;
        await db.SaveChangesAsync(cancellationToken);
        return Results.Ok(ToResponse(monitor, schedule));
    }

    private static async Task<IResult> SetStatusAsync(
        HttpContext http, ApplicationDbContext db, Guid id, MonitorStatusRequest request, TimeProvider clock, CheckSchedule schedule,
        CancellationToken cancellationToken)
    {
        if (request.Status is not ("active" or "paused"))
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["status"] = ["O status deve ser ativo ou pausado."]
            }, title: "Uma ou mais informações são inválidas.");
        var monitor = await OwnedMonitorAsync(db, UserId(http), id, cancellationToken);
        if (monitor is null) return NotFound();
        if (monitor.Status != request.Status)
        {
            var now = clock.GetUtcNow().UtcDateTime;
            monitor.Status = request.Status;
            monitor.NextCheckAt = request.Status == "active" ? now : null;
            monitor.UpdatedAt = now;
            await db.SaveChangesAsync(cancellationToken);
        }
        return Results.Ok(ToResponse(monitor, schedule));
    }

    private static async Task<IResult> DeleteAsync(
        HttpContext http, ApplicationDbContext db, Guid id, CancellationToken cancellationToken)
    {
        var userId = UserId(http);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var monitor = await db.Monitors.FromSqlInterpolated(
            $"SELECT * FROM \"Monitors\" WHERE \"Id\" = {id} AND \"UserId\" = {userId} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);
        if (monitor is null) return NotFound();
        await db.Notifications.Where(x => x.ChangeEvent.MonitorId == id).ExecuteDeleteAsync(cancellationToken);
        await db.ChangeEvents.Where(x => x.MonitorId == id).ExecuteDeleteAsync(cancellationToken);
        await db.Snapshots.Where(x => x.MonitorId == id).ExecuteDeleteAsync(cancellationToken);
        db.Monitors.Remove(monitor);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Results.NoContent();
    }

    private static Task<Monitor?> OwnedMonitorAsync(ApplicationDbContext db, string userId, Guid id,
        CancellationToken cancellationToken) => db.Monitors.SingleOrDefaultAsync(
            m => m.Id == id && m.UserId == userId, cancellationToken);

    private static string UserId(HttpContext http) => http.User.FindFirstValue(ClaimTypes.NameIdentifier)!;
    private static IResult NotFound() => Results.Problem(statusCode: 404, title: "Monitoramento não encontrado.");
    private static MonitorResponse ToResponse(Monitor m, CheckSchedule schedule) => new(m.Id, m.Name, m.Url,
        m.IntervalMinutes, m.Status, schedule.NextCheckAt(m.NextCheckAt, m.IntervalMinutes), m.CreatedAt, m.UpdatedAt,
        null, m.ContentSelector, m.KeywordsJson, MonitorRules.ReadKeywords(m.KeywordsJson), m.EmailAlertsEnabled);

    public sealed record MonitorRequest(string? Name, string? Url, int IntervalMinutes,
        string? ContentSelector = null, string[]? Keywords = null, bool? EmailAlertsEnabled = null);
    public sealed record MonitorStatusRequest(string? Status);
    public sealed record MonitorResponse(Guid Id, string Name, string Url, int IntervalMinutes,
        string Status, DateTime? NextCheckAt, DateTime CreatedAt, DateTime UpdatedAt,
        DateTime? LastCheckedAt = null, string? ContentSelector = null,
        [property: System.Text.Json.Serialization.JsonIgnore] string? KeywordsJson = null,
        string[]? Keywords = null, bool EmailAlertsEnabled = false);
    public sealed record MonitorPageResponse(IReadOnlyList<MonitorResponse> Items, int Page,
        int PageSize, int TotalCount);
}
