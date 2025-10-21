using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using WebChangeRadar.Api.Data;
using WebChangeRadar.Api.Features.Checks;
using WebChangeRadar.Api.Features.Monitors;
using WebChangeRadar.Api.Features.Notifications;
using WebChangeRadar.Api.Services.Extraction;
using WebChangeRadar.Api.Services.Fetching;
using Xunit;
using Monitor = WebChangeRadar.Api.Data.Monitor;

namespace WebChangeRadar.IntegrationTests;

public sealed class V1FlowTests(PostgreSqlFixture database) : IClassFixture<PostgreSqlFixture>
{
    [PostgreSqlFact]
    public async Task SelectorKeywordsAndFileEmailFollowTheWholePersistedFlow()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"webchange-flow-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var now = DateTime.UtcNow;
            var recipient = $"flow-{Guid.NewGuid():N}@example.test";
            var user = new ApplicationUser { UserName = recipient, Email = recipient, EmailConfirmed = true };
            var monitor = new Monitor { Id = Guid.NewGuid(), UserId = user.Id, Name = "Release page",
                Url = "https://example.org/release", IntervalMinutes = 60, ContentSelector = "h1",
                KeywordsJson = MonitorRules.SerializeKeywords(["ALERTA"]), EmailAlertsEnabled = true,
                NextCheckAt = now.AddHours(1), CreatedAt = now, UpdatedAt = now };
            await using (var db = database.CreateContext())
            {
                db.Users.Add(user);
                db.Monitors.Add(monitor);
                await db.SaveChangesAsync();
            }
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Email:Mode"] = "File", ["Email:FileDirectory"] = directory,
                ["Email:PublicBaseUrl"] = "https://localhost:3000"
            }).Build();
            var environment = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
                { EnvironmentName = Environments.Development }).Environment;
            var sender = new SmtpChangeEmailSender(config, environment);
            Assert.True(sender.IsConfigured);

            await Check("<h1>Version 3</h1><p>Old</p>");
            await Counts(1, 0, 0, 0, 0);
            await Check("<h1>Version 3</h1><p>ALERTA outside</p>");
            await Counts(1, 0, 0, 0, 0);
            await Check("<h1>Version 4</h1><p>ALERTA outside</p>");
            await Counts(2, 1, 0, 0, 0);
            await Check("<h1> ALERTA: version 5 </h1><p>Old</p>");
            await Counts(3, 2, 1, 1, 1);
            string file = Assert.Single(Directory.GetFiles(directory, "*.txt"));
            var message = await File.ReadAllTextAsync(file);
            Assert.Contains($"To: {recipient}", message);
            Assert.Contains("Subject: Mudança detectada: Release page", message);
            Assert.Contains("https://localhost:3000/changes/", message);
            Assert.Contains(monitor.Url, message);

            await Check("<h1>ALERTA:  version 5</h1><p>Unimportant</p>");
            await Counts(3, 2, 1, 1, 1);
            await using (var db = database.CreateContext())
            {
                (await db.Monitors.SingleAsync(x => x.Id == monitor.Id)).EmailAlertsEnabled = false;
                await db.SaveChangesAsync();
            }
            await Check("<h1>ALERTA: version 6</h1>");
            await Counts(4, 3, 2, 1, 1);
            await Check(null);
            await Counts(4, 3, 2, 1, 1);
            await using (var db = database.CreateContext())
            {
                var saved = await db.Monitors.SingleAsync(x => x.Id == monitor.Id);
                Assert.Equal(6, await db.CheckRuns.CountAsync(x => x.MonitorId == monitor.Id && x.Status == "success"));
                Assert.Equal("ALERTA: version 6", (await db.Snapshots.SingleAsync(x => x.Id == saved.CurrentSnapshotId)).NormalizedText);
                Assert.Equal("http_error", (await db.CheckRuns.Where(x => x.MonitorId == monitor.Id)
                    .OrderByDescending(x => x.RequestedAt).FirstAsync()).ErrorCode);
            }

            async Task Check(string? html)
            {
                var fetcher = new StubFetcher(html, monitor.Url);
                await using var db = database.CreateContext();
                db.CheckRuns.Add(new CheckRun { Id = Guid.NewGuid(), MonitorId = monitor.Id,
                    RequestedAt = DateTime.UtcNow });
                await db.SaveChangesAsync();
                var schedule = new CheckSchedule(new ConfigurationBuilder().Build(),
                    Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
                        { EnvironmentName = Environments.Production }).Environment);
                Assert.Equal(1, await new CheckProcessor(db, fetcher, new ContentExtractor(),
                    TimeProvider.System, schedule).ProcessAsync(default));
                await using var mailDb = database.CreateContext();
                await new EmailOutboxProcessor(mailDb, sender, TimeProvider.System,
                    NullLogger<EmailOutboxProcessor>.Instance).ProcessAsync(default);
            }

            async Task Counts(int snapshots, int changes, int notices, int outbox, int files)
            {
                await using var db = database.CreateContext();
                Assert.Equal(snapshots, await db.Snapshots.CountAsync(x => x.MonitorId == monitor.Id));
                Assert.Equal(changes, await db.ChangeEvents.CountAsync(x => x.MonitorId == monitor.Id));
                Assert.Equal(notices, await db.Notifications.CountAsync(x => x.UserId == user.Id));
                Assert.Equal(outbox, await db.EmailOutbox.CountAsync(x => x.UserId == user.Id));
                Assert.Equal(files, Directory.GetFiles(directory, "*.txt").Length);
            }
        }
        finally
        {
            foreach (var file in Directory.GetFiles(directory)) File.Delete(file);
            Directory.Delete(directory);
        }
    }

    private sealed class StubFetcher(string? html, string url) : IPageFetcher
    {
        public Task<PageFetchResult> FetchAsync(string ignored, CancellationToken cancellationToken = default) =>
            Task.FromResult(html is null
                ? new PageFetchResult(null, null, "http_error", HttpStatusCode.ServiceUnavailable)
                : new PageFetchResult(html, new Uri(url), null, HttpStatusCode.OK));
    }
}
