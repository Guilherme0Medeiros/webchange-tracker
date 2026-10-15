using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using WebChangeRadar.Api.Data;
using WebChangeRadar.Api.Features.Auth;

namespace WebChangeRadar.Api.Features.Checks;

public static class CheckProcessingEndpoints
{
    public static void MapCheckProcessingEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/monitors/{id:guid}/checks", QueueAsync)
            .RequireAuthorization().AddEndpointFilter<RequireCsrfTokenFilter>();
        app.MapPost("/internal/jobs/process-due", ProcessAsync);
    }

    private static async Task<IResult> QueueAsync(HttpContext http, ApplicationDbContext db, Guid id,
        TimeProvider clock, IConfiguration configuration, IHostEnvironment environment, CancellationToken ct)
    {
        var userId = http.User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var monitor = await db.Monitors.FromSqlInterpolated(
            $"SELECT * FROM \"Monitors\" WHERE \"Id\" = {id} AND \"UserId\" = {userId} FOR UPDATE")
            .SingleOrDefaultAsync(ct);
        if (monitor is null) return Results.Problem(statusCode: 404, title: "Resource not found.");
        if (monitor.Status != "active") return Results.Problem(statusCode: 409, title: "Monitor is paused.");
        var now = clock.GetUtcNow().UtcDateTime;
        var configuredCooldown = configuration.GetValue<int?>("ManualCheckCooldownMinutes") ?? 5;
        var cooldownMinutes = configuredCooldown < 0 ? 5 :
            environment.IsDevelopment() ? configuredCooldown : Math.Max(configuredCooldown, 5);
        if (cooldownMinutes > 0 && await db.CheckRuns.AnyAsync(x => x.MonitorId == id &&
            x.TriggerType == "manual" && x.RequestedAt > now.AddMinutes(-cooldownMinutes), ct))
            return Results.Problem(statusCode: 429, title: "Manual check limit reached.");
        var run = new CheckRun { Id = Guid.NewGuid(), MonitorId = id, TriggerType = "manual", RequestedAt = now };
        db.CheckRuns.Add(run);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return Results.Accepted($"/api/monitors/{id}/checks", new { run.Id, run.Status });
    }

    private static async Task<IResult> ProcessAsync(HttpContext http, IConfiguration configuration,
        CheckProcessor processor, CancellationToken ct)
    {
        var configured = configuration["InternalJobs:Token"];
        var supplied = http.Request.Headers.Authorization.ToString();
        if (string.IsNullOrWhiteSpace(configured) || !supplied.StartsWith("Bearer ", StringComparison.Ordinal) ||
            !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(configured),
                Encoding.UTF8.GetBytes(supplied[7..])))
            return Results.Unauthorized();
        return Results.Ok(new { processed = await processor.ProcessAsync(ct) });
    }
}
