using System.Net;
using Microsoft.EntityFrameworkCore;
using WebChangeRadar.Api.Data;
using WebChangeRadar.Api.Features.Checks;
using WebChangeRadar.Api.Services.Extraction;
using WebChangeRadar.Api.Services.Fetching;
using Xunit;
using Monitor = WebChangeRadar.Api.Data.Monitor;

namespace WebChangeRadar.IntegrationTests;

public sealed class CheckProcessingTests(PostgreSqlFixture database) : IClassFixture<PostgreSqlFixture>
{
    [PostgreSqlFact]
    public async Task ExpiredLeaseCanBeReclaimedWithoutOldOwnerWriting()
    {
        var user = new ApplicationUser { UserName = $"lease-{Guid.NewGuid():N}@example.test" };
        var monitor = new Monitor { Id = Guid.NewGuid(), UserId = user.Id, Name = "Lease",
            Url = "https://example.org", IntervalMinutes = 60, Status = "active",
            NextCheckAt = DateTime.UtcNow.AddHours(1), CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow };
        var run = new CheckRun { Id = Guid.NewGuid(), MonitorId = monitor.Id,
            RequestedAt = DateTime.UtcNow };
        await using (var db = database.CreateContext())
        {
            db.Users.Add(user);
            db.Monitors.Add(monitor);
            db.CheckRuns.Add(run);
            await db.SaveChangesAsync();
        }
        var firstFetcher = new WaitingFetcher();
        var first = Task.Run(async () =>
        {
            await using var db = database.CreateContext();
            await new CheckProcessor(db, firstFetcher, new ContentExtractor(), TimeProvider.System)
                .ProcessAsync(default);
        });
        await firstFetcher.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await using (var db = database.CreateContext())
        {
            var claimed = await db.CheckRuns.SingleAsync(x => x.Id == run.Id);
            Assert.Equal("running", claimed.Status);
            claimed.LeaseExpiresAt = DateTime.UtcNow.AddSeconds(-1);
            await db.SaveChangesAsync();
        }
        await using (var db = database.CreateContext())
            await new CheckProcessor(db, new StubFetcher
            {
                Result = new PageFetchResult("<p>Second owner</p>", new Uri(monitor.Url), null, HttpStatusCode.OK)
            }, new ContentExtractor(), TimeProvider.System).ProcessAsync(default);
        firstFetcher.Release.SetResult();
        await first;
        await using (var db = database.CreateContext())
        {
            Assert.Equal("success", (await db.CheckRuns.SingleAsync(x => x.Id == run.Id)).Status);
            Assert.Equal("Second owner", (await db.Snapshots.SingleAsync(x => x.MonitorId == monitor.Id)).NormalizedText);
            Assert.Empty(await db.ChangeEvents.Where(x => x.MonitorId == monitor.Id).ToListAsync());
            Assert.Empty(await db.Notifications.Where(x => x.UserId == user.Id).ToListAsync());
        }
    }

    [PostgreSqlFact]
    public async Task BaselineIdenticalChangeFailureAndUrlReset()
    {
        var user = new ApplicationUser { UserName = $"checks-{Guid.NewGuid():N}@example.test" };
        var monitor = new Monitor { Id = Guid.NewGuid(), UserId = user.Id, Name = "Page",
            Url = "https://example.org", IntervalMinutes = 60, Status = "active",
            NextCheckAt = DateTime.UtcNow.AddMinutes(-1), CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow };
        await using (var db = database.CreateContext())
        {
            db.Users.Add(user);
            db.Monitors.Add(monitor);
            await db.SaveChangesAsync();
        }

        var fetcher = new StubFetcher();
        await Process(fetcher, "<p>Hello world</p>");
        await AssertCounts(1, 0, 0, 1, 0);
        await Process(fetcher, "<p>  Hello   world </p>");
        await AssertCounts(1, 0, 0, 2, 0);
        await Process(fetcher, "<p>Goodbye world</p>");
        await AssertCounts(2, 1, 1, 3, 0);
        await Process(fetcher, null);
        await AssertCounts(2, 1, 1, 4, 1);

        await using (var db = database.CreateContext())
        {
            var current = await db.Monitors.SingleAsync(x => x.Id == monitor.Id);
            current.Url = "https://example.net";
            current.BaselineResetAt = DateTime.UtcNow;
            current.CurrentSnapshotId = null;
            db.CheckRuns.Add(new CheckRun { Id = Guid.NewGuid(), MonitorId = monitor.Id,
                RequestedAt = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }
        fetcher.Result = new PageFetchResult("<p>New site</p>", new Uri("https://example.net"), null, HttpStatusCode.OK);
        await using (var db = database.CreateContext())
            await new CheckProcessor(db, fetcher, new ContentExtractor(), TimeProvider.System).ProcessAsync(default);
        await AssertCounts(3, 1, 1, 5, 1);

        async Task Process(StubFetcher stub, string? html)
        {
            stub.Result = html is null
                ? new PageFetchResult(null, null, "http_error", HttpStatusCode.ServiceUnavailable)
                : new PageFetchResult(html, new Uri("https://example.org"), null, HttpStatusCode.OK);
            await using var db = database.CreateContext();
            if (await db.CheckRuns.AnyAsync(x => x.MonitorId == monitor.Id))
            {
                db.CheckRuns.Add(new CheckRun { Id = Guid.NewGuid(), MonitorId = monitor.Id,
                    RequestedAt = DateTime.UtcNow });
                await db.SaveChangesAsync();
            }
            await new CheckProcessor(db, stub, new ContentExtractor(), TimeProvider.System).ProcessAsync(default);
        }

        async Task AssertCounts(int snapshots, int changes, int notifications, int runs, int failures)
        {
            await using var db = database.CreateContext();
            Assert.Equal(snapshots, await db.Snapshots.CountAsync(x => x.MonitorId == monitor.Id));
            Assert.Equal(changes, await db.ChangeEvents.CountAsync(x => x.MonitorId == monitor.Id));
            Assert.Equal(notifications, await db.Notifications.CountAsync(x => x.UserId == user.Id));
            Assert.Equal(runs, await db.CheckRuns.CountAsync(x => x.MonitorId == monitor.Id));
            Assert.Equal(failures, await db.CheckRuns.CountAsync(x => x.MonitorId == monitor.Id && x.Status == "failed"));
        }
    }

    private sealed class StubFetcher : IPageFetcher
    {
        public PageFetchResult Result { get; set; } = null!;
        public Task<PageFetchResult> FetchAsync(string url, CancellationToken cancellationToken = default) =>
            Task.FromResult(Result);
    }

    private sealed class WaitingFetcher : IPageFetcher
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<PageFetchResult> FetchAsync(string url, CancellationToken cancellationToken = default)
        {
            Started.SetResult();
            await Release.Task.WaitAsync(cancellationToken);
            return new PageFetchResult("<p>Old owner</p>", new Uri(url), null, HttpStatusCode.OK);
        }
    }
}
