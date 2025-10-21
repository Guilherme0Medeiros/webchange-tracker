using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using WebChangeRadar.Api.Features.Notifications;
using Xunit;

namespace WebChangeRadar.UnitTests;

public sealed class EmailSenderTests
{
    [Fact]
    public async Task FileModeWritesConfirmationToConfiguredTemporaryDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"webchange-email-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var sender = Sender(Environments.Development, new Dictionary<string, string?>
            {
                ["Email:Mode"] = "File", ["Email:FileDirectory"] = directory,
                ["Email:PublicBaseUrl"] = "https://localhost:3000"
            });
            Assert.True(sender.IsConfigured);
            await sender.SendConfirmationAsync("account@example.test",
                "https://localhost:3000/api/notifications/confirm-email?token=test", default);
            var content = await File.ReadAllTextAsync(Assert.Single(Directory.GetFiles(directory)));
            Assert.Contains("To: account@example.test", content);
            Assert.Contains("Subject: Confirme seu e-mail", content);
            Assert.Contains("confirm-email?token=test", content);
        }
        finally
        {
            foreach (var file in Directory.GetFiles(directory)) File.Delete(file);
            Directory.Delete(directory);
        }
    }

    [Fact]
    public async Task FileWriteFailureIsObservableWithoutCreatingAnAlertFile()
    {
        var path = Path.Combine(Path.GetTempPath(), $"webchange-email-blocked-{Guid.NewGuid():N}.txt");
        await File.WriteAllTextAsync(path, "existing file");
        try
        {
            var sender = Sender(Environments.Development, new Dictionary<string, string?>
            {
                ["Email:Mode"] = "File", ["Email:FileDirectory"] = path,
                ["Email:PublicBaseUrl"] = "https://localhost:3000"
            });
            Assert.True(sender.IsConfigured);
            await Assert.ThrowsAnyAsync<IOException>(() => sender.SendAsync(
                new ChangeEmail("account@example.test", "Monitor", "https://example.org",
                    Guid.NewGuid(), DateTime.UtcNow), default));
            Assert.Equal("existing file", await File.ReadAllTextAsync(path));
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData("Production", "File", "https://localhost:3000")]
    [InlineData("Development", "File", "http://localhost:3000")]
    [InlineData("Development", "Smtp", "https://localhost:3000")]
    public async Task InvalidOrUnavailableConfigurationDoesNotSend(string environment,
        string mode, string baseUrl)
    {
        var sender = Sender(environment, new Dictionary<string, string?>
        {
            ["Email:Mode"] = mode, ["Email:FileDirectory"] = "unused",
            ["Email:PublicBaseUrl"] = baseUrl
        });
        Assert.False(sender.IsConfigured);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => sender.SendAsync(
            new ChangeEmail("account@example.test", "Monitor", "https://example.org",
                Guid.NewGuid(), DateTime.UtcNow), default));
        Assert.Equal("email_not_configured", error.Message);
    }

    private static SmtpChangeEmailSender Sender(string environment, Dictionary<string, string?> values) =>
        new(new ConfigurationBuilder().AddInMemoryCollection(values).Build(),
            Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
                { EnvironmentName = environment }).Environment);
}
