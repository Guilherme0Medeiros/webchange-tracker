using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using WebChangeRadar.Api.Data;
using WebChangeRadar.Api.Features.Notifications;
using Xunit;
using Monitor = WebChangeRadar.Api.Data.Monitor;

namespace WebChangeRadar.IntegrationTests;

public sealed class EmailOutboxReliabilityTests(PostgreSqlFixture database) : IClassFixture<PostgreSqlFixture>
{
    [PostgreSqlFact]
    public async Task FailedAttemptsAreDurableAndFollowBackoffUntilExhausted()
    {
        var now = DateTime.UtcNow;
        var id = await SeedAsync(now);
        var clock = new AdjustableClock(now.AddSeconds(1));
        var sender = new RecordingSender { Fail = true };
        var delays = new[] { 1, 3, 9, 27 };
        for (var attempt = 1; attempt <= 5; attempt++)
        {
            Assert.Equal(1, await ProcessAsync(sender, clock));
            await using var db = database.CreateContext();
            var row = await db.EmailOutbox.SingleAsync(x => x.Id == id);
            Assert.Equal(attempt, row.Attempts);
            Assert.Equal("provider_error", row.LastErrorCode);
            Assert.Null(row.LeaseToken);
            Assert.Null(row.LeaseExpiresAt);
            if (attempt == 5)
            {
                Assert.Equal("failed", row.Status);
            }
            else
            {
                Assert.Equal("pending", row.Status);
                Assert.InRange(row.NextAttemptAt,
                    clock.GetUtcNow().UtcDateTime.AddMinutes(delays[attempt - 1]).AddMilliseconds(-1),
                    clock.GetUtcNow().UtcDateTime.AddMinutes(delays[attempt - 1]).AddMilliseconds(1));
                Assert.Equal(0, await ProcessAsync(sender, clock));
                clock.Advance(TimeSpan.FromMinutes(delays[attempt - 1]));
            }
        }
        Assert.Equal(5, sender.Attempts);
        clock.Advance(TimeSpan.FromDays(1));
        Assert.Equal(0, await ProcessAsync(sender, clock));
        Assert.Equal(5, sender.Attempts);
    }

    [PostgreSqlFact]
    public async Task TwoWorkersDoNotClaimSameActiveLeaseAndExpiredLeaseCanBeRecovered()
    {
        var now = DateTime.UtcNow;
        var id = await SeedAsync(now);
        var clock = new AdjustableClock(now.AddSeconds(1));
        var heldSender = new RecordingSender { Wait = true };
        var first = ProcessAsync(heldSender, clock);
        await heldSender.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            var otherSender = new RecordingSender();
            Assert.Equal(0, await ProcessAsync(otherSender, clock));
            Assert.Equal(0, otherSender.Attempts);
            await using (var db = database.CreateContext())
            {
                var claimed = await db.EmailOutbox.SingleAsync(x => x.Id == id);
                Assert.Equal("sending", claimed.Status);
                Assert.Equal(1, claimed.Attempts);
            }
            clock.Advance(TimeSpan.FromMinutes(3));
            Assert.Equal(1, await ProcessAsync(otherSender, clock));
            Assert.Equal(1, otherSender.Attempts);
            await using (var db = database.CreateContext())
            {
                var recovered = await db.EmailOutbox.SingleAsync(x => x.Id == id);
                Assert.Equal("sent", recovered.Status);
                Assert.Equal(2, recovered.Attempts);
            }
        }
        finally { heldSender.Release.SetResult(); }
        await first;
        await using var finalDb = database.CreateContext();
        Assert.Equal("sent", (await finalDb.EmailOutbox.SingleAsync(x => x.Id == id)).Status);
        // A provider may still deliver after a lease expires; this is an at-least-once queue.
        Assert.Equal(1, heldSender.Attempts);
    }

    [PostgreSqlFact]
    public async Task OptOutAtDeliveryAndExpiredPendingRowsAreNotSent()
    {
        var now = DateTime.UtcNow;
        var ineligibleId = await SeedAsync(now);
        var expiredId = await SeedAsync(now.AddDays(-2));
        await using (var db = database.CreateContext())
        {
            var row = await db.EmailOutbox.Include(x => x.ChangeEvent).SingleAsync(x => x.Id == ineligibleId);
            (await db.Monitors.SingleAsync(x => x.Id == row.ChangeEvent.MonitorId)).EmailAlertsEnabled = false;
            await db.SaveChangesAsync();
        }
        var sender = new RecordingSender();
        Assert.Equal(1, await ProcessAsync(sender, new AdjustableClock(now.AddSeconds(1))));
        Assert.Equal(0, sender.Attempts);
        await using var saved = database.CreateContext();
        var ineligible = await saved.EmailOutbox.SingleAsync(x => x.Id == ineligibleId);
        var expired = await saved.EmailOutbox.SingleAsync(x => x.Id == expiredId);
        Assert.Equal("failed", ineligible.Status);
        Assert.Equal("recipient_ineligible", ineligible.LastErrorCode);
        Assert.Equal("failed", expired.Status);
        Assert.Equal("expired", expired.LastErrorCode);
    }

    private async Task<Guid> SeedAsync(DateTime when)
    {
        var email = $"outbox-{Guid.NewGuid():N}@example.test";
        var user = new ApplicationUser { UserName = email, Email = email, EmailConfirmed = true };
        var monitor = new Monitor { Id = Guid.NewGuid(), UserId = user.Id, Name = "Mail test",
            Url = "https://example.org", IntervalMinutes = 60, EmailAlertsEnabled = true,
            CreatedAt = when, UpdatedAt = when, NextCheckAt = when.AddHours(1) };
        var oldSnapshot = new Snapshot { Id = Guid.NewGuid(), MonitorId = monitor.Id,
            ContentHash = "old", NormalizedText = "old", CapturedAt = when };
        var newSnapshot = new Snapshot { Id = Guid.NewGuid(), MonitorId = monitor.Id,
            ContentHash = "new", NormalizedText = "new", CapturedAt = when };
        var change = new ChangeEvent { Id = Guid.NewGuid(), MonitorId = monitor.Id,
            OldSnapshotId = oldSnapshot.Id, NewSnapshotId = newSnapshot.Id, DetectedAt = when };
        var outbox = new EmailOutbox { Id = Guid.NewGuid(), ChangeEventId = change.Id,
            UserId = user.Id, Recipient = email, NextAttemptAt = when };
        await using var db = database.CreateContext();
        db.Users.Add(user);
        db.Monitors.Add(monitor);
        db.Snapshots.AddRange(oldSnapshot, newSnapshot);
        db.ChangeEvents.Add(change);
        db.EmailOutbox.Add(outbox);
        await db.SaveChangesAsync();
        return outbox.Id;
    }

    private async Task<int> ProcessAsync(IChangeEmailSender sender, TimeProvider clock)
    {
        await using var db = database.CreateContext();
        return await new EmailOutboxProcessor(db, sender, clock,
            NullLogger<EmailOutboxProcessor>.Instance).ProcessAsync(default);
    }

    private sealed class AdjustableClock(DateTime now) : TimeProvider
    {
        private DateTimeOffset _now = new(now, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan duration) => _now += duration;
    }

    private sealed class RecordingSender : IChangeEmailSender
    {
        public bool IsConfigured => true;
        public bool Fail { get; set; }
        public bool Wait { get; set; }
        public int Attempts { get; private set; }
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task SendAsync(ChangeEmail email, CancellationToken cancellationToken)
        {
            Attempts++;
            Started.TrySetResult();
            if (Wait) await Release.Task.WaitAsync(cancellationToken);
            if (Fail) throw new InvalidOperationException("simulated provider failure");
        }
        public Task SendConfirmationAsync(string recipient, string link, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }
}
