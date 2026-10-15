using Microsoft.EntityFrameworkCore;
using WebChangeRadar.Api.Data;
using WebChangeRadar.Api.Features.Monitors;
using WebChangeRadar.Api.Services.Comparison;
using WebChangeRadar.Api.Services.Extraction;
using WebChangeRadar.Api.Services.Fetching;

namespace WebChangeRadar.Api.Features.Checks;

public sealed class CheckProcessor(ApplicationDbContext db, IPageFetcher fetcher,
    IContentExtractor extractor, TimeProvider clock, CheckSchedule schedule)
{
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(2);
    private const int BatchSize = 10;

    public async Task<int> ProcessAsync(CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var developmentInterval = schedule.DevelopmentIntervalMinutes ?? 0;
        // Lock due monitors while making work, so concurrent scheduler calls cannot enqueue twice.
        await using (var transaction = await db.Database.BeginTransactionAsync(cancellationToken))
        {
            var due = await db.Monitors.FromSqlInterpolated(
                $"""
                SELECT * FROM "Monitors"
                WHERE "Status" = 'active' AND
                    ("NextCheckAt" <= {now} OR
                     ({developmentInterval} > 0 AND "NextCheckAt" - make_interval(mins => "IntervalMinutes") +
                      make_interval(mins => {developmentInterval}) <= {now}))
                ORDER BY "NextCheckAt" LIMIT 10 FOR UPDATE SKIP LOCKED
                """)
                .ToListAsync(cancellationToken);
            foreach (var monitor in due)
            {
                if (!await db.CheckRuns.AnyAsync(x => x.MonitorId == monitor.Id &&
                    (x.Status == "pending" || x.Status == "running"), cancellationToken))
                    db.CheckRuns.Add(new CheckRun { Id = Guid.NewGuid(), MonitorId = monitor.Id,
                        RequestedAt = now });
                monitor.NextCheckAt = now.AddMinutes(monitor.IntervalMinutes);
            }
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        var processed = 0;
        for (var i = 0; i < BatchSize; i++)
        {
            var claim = await ClaimAsync(cancellationToken);
            if (claim is null) break;
            await ExecuteAsync(claim.Value, cancellationToken);
            processed++;
        }
        return processed;
    }

    private async Task<(Guid Id, Guid Token, string Url, DateTime? ResetAt)?> ClaimAsync(CancellationToken cancellationToken)
    {
        db.ChangeTracker.Clear();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var now = clock.GetUtcNow().UtcDateTime;
        var run = await db.CheckRuns.FromSqlRaw(
            "SELECT * FROM \"CheckRuns\" WHERE \"Status\" = 'pending' OR (\"Status\" = 'running' AND \"LeaseExpiresAt\" < {0}) ORDER BY \"RequestedAt\" LIMIT 1 FOR UPDATE SKIP LOCKED", now)
            .FirstOrDefaultAsync(cancellationToken);
        if (run is null) return null;
        var monitor = await db.Monitors.SingleAsync(x => x.Id == run.MonitorId, cancellationToken);
        var token = Guid.NewGuid();
        run.Status = "running";
        run.StartedAt = now;
        run.LeaseToken = token;
        run.LeaseExpiresAt = now.Add(LeaseDuration);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        db.ChangeTracker.Clear(); // The lease may change in another worker during the fetch.
        return (run.Id, token, monitor.Url, monitor.BaselineResetAt);
    }

    private async Task ExecuteAsync((Guid Id, Guid Token, string Url, DateTime? ResetAt) claim,
        CancellationToken cancellationToken)
    {
        PageFetchResult result;
        try { result = await fetcher.FetchAsync(claim.Url, cancellationToken); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            result = new PageFetchResult(null, null, "fetch_error", null);
        }
        var text = result.IsSuccess ? extractor.Extract(result.Html) : null;
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var run = await db.CheckRuns.FromSqlRaw(
            "SELECT * FROM \"CheckRuns\" WHERE \"Id\" = {0} FOR UPDATE", claim.Id)
            .SingleAsync(cancellationToken);
        if (run.LeaseToken != claim.Token || run.Status != "running") return;
        var monitor = await db.Monitors.FromSqlRaw(
            "SELECT * FROM \"Monitors\" WHERE \"Id\" = {0} FOR UPDATE", run.MonitorId)
            .SingleAsync(cancellationToken);
        var now = clock.GetUtcNow().UtcDateTime;
        run.FinishedAt = now;
        run.LeaseToken = null;
        run.LeaseExpiresAt = null;
        run.HttpStatusCode = (int?)result.StatusCode;
        if (monitor.Url != claim.Url || monitor.BaselineResetAt != claim.ResetAt)
        {
            run.Status = "stale";
        }
        else if (!result.IsSuccess)
        {
            run.Status = "failed";
            run.ErrorCode = result.ErrorCode;
        }
        else
        {
            run.Status = "success";
            var previous = monitor.CurrentSnapshotId is Guid snapshotId
                ? await db.Snapshots.SingleAsync(x => x.Id == snapshotId, cancellationToken)
                : null;
            var detection = ChangeDetector.Detect(text!, previous?.ContentHash);
            if (detection.Kind != ContentChange.Unchanged)
            {
                var snapshot = new Snapshot { Id = Guid.NewGuid(), MonitorId = run.MonitorId,
                    ContentHash = detection.ContentHash, NormalizedText = text!, CapturedAt = now };
                db.Snapshots.Add(snapshot);
                monitor.CurrentSnapshotId = snapshot.Id;
                if (detection.Kind == ContentChange.Changed)
                {
                    var change = new ChangeEvent { Id = Guid.NewGuid(), MonitorId = run.MonitorId,
                        OldSnapshotId = previous!.Id, NewSnapshotId = snapshot.Id, DetectedAt = now };
                    db.ChangeEvents.Add(change);
                    db.Notifications.Add(new Notification { Id = Guid.NewGuid(), UserId = monitor.UserId,
                        ChangeEventId = change.Id, CreatedAt = now });
                }
            }
            if (claim.ResetAt is not null) monitor.BaselineResetAt = null;
        }
        if (monitor.Status == "active" && monitor.NextCheckAt <= now)
            monitor.NextCheckAt = now.AddMinutes(monitor.IntervalMinutes);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
}
