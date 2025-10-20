using System.Net;
using System.Net.Mail;
using System.Text;
using Microsoft.EntityFrameworkCore;
using WebChangeRadar.Api.Data;

namespace WebChangeRadar.Api.Features.Notifications;

public sealed record ChangeEmail(string Recipient, string MonitorName, string Url,
    Guid ChangeId, DateTime DetectedAt);

public interface IChangeEmailSender
{
    bool IsConfigured { get; }
    Task SendAsync(ChangeEmail email, CancellationToken cancellationToken);
    Task SendConfirmationAsync(string recipient, string link, CancellationToken cancellationToken);
}

public sealed class SmtpChangeEmailSender(IConfiguration configuration, IHostEnvironment environment) : IChangeEmailSender
{
    private bool ValidBaseUrl => Uri.TryCreate(configuration["Email:PublicBaseUrl"], UriKind.Absolute,
        out var uri) && uri.Scheme == Uri.UriSchemeHttps;
    private bool FileMode => environment.IsDevelopment() &&
        configuration["Email:Mode"] == "File" &&
        !string.IsNullOrWhiteSpace(configuration["Email:FileDirectory"]);
    public bool IsConfigured => ValidBaseUrl && (FileMode ||
        configuration["Email:Mode"] == "Smtp" &&
        !string.IsNullOrWhiteSpace(configuration["Email:Host"]) &&
        !string.IsNullOrWhiteSpace(configuration["Email:From"]) &&
        !string.IsNullOrWhiteSpace(configuration["Email:Username"]) &&
        !string.IsNullOrWhiteSpace(configuration["Email:Password"]));

    public async Task SendAsync(ChangeEmail email, CancellationToken cancellationToken)
    {
        if (!IsConfigured) throw new InvalidOperationException("email_not_configured");
        var link = new Uri(new Uri(configuration["Email:PublicBaseUrl"]!),
            $"/changes/{email.ChangeId}").ToString();
        var subject = $"Mudança detectada: {email.MonitorName.Replace('\r', ' ').Replace('\n', ' ')}";
        var plain = $"Mudança detectada em {email.MonitorName}\nURL: {email.Url}\n" +
            $"Detectada em: {email.DetectedAt:u}\nDetalhes: {link}";
        var html = $"<p>Mudança detectada em {WebUtility.HtmlEncode(email.MonitorName)}</p>" +
            $"<p>URL: {WebUtility.HtmlEncode(email.Url)}</p>" +
            $"<p>Detectada em: {WebUtility.HtmlEncode(email.DetectedAt.ToString("u"))}</p>" +
            $"<p><a href=\"{WebUtility.HtmlEncode(link)}\">Ver detalhes</a></p>";
        if (FileMode)
        {
            await WriteFileAsync(email.Recipient, subject, plain + "\n\nHTML:\n" + html,
                cancellationToken);
            return;
        }
        using var message = new MailMessage(configuration["Email:From"]!, email.Recipient)
        {
            Subject = subject,
            SubjectEncoding = Encoding.UTF8,
            Body = plain,
            BodyEncoding = Encoding.UTF8
        };
        message.Headers.Add("Message-ID", $"<change-{email.ChangeId:N}@webchange-radar.local>");
        message.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(html, Encoding.UTF8, "text/html"));
        using var client = new SmtpClient(configuration["Email:Host"]!,
            configuration.GetValue<int?>("Email:Port") ?? 587)
        {
            EnableSsl = true,
            Credentials = new NetworkCredential(configuration["Email:Username"], configuration["Email:Password"]),
            Timeout = 10000
        };
        await client.SendMailAsync(message, cancellationToken);
    }

    public async Task SendConfirmationAsync(string recipient, string link, CancellationToken cancellationToken)
    {
        if (!IsConfigured) throw new InvalidOperationException("email_not_configured");
        if (FileMode)
        {
            await WriteFileAsync(recipient, "Confirme seu e-mail no WebChange Radar",
                $"Confirme seu endereço para receber alertas: {link}", cancellationToken);
            return;
        }
        using var message = new MailMessage(configuration["Email:From"]!, recipient)
        {
            Subject = "Confirme seu e-mail no WebChange Radar",
            SubjectEncoding = Encoding.UTF8,
            Body = $"Confirme seu endereço para receber alertas: {link}",
            BodyEncoding = Encoding.UTF8
        };
        using var client = new SmtpClient(configuration["Email:Host"]!,
            configuration.GetValue<int?>("Email:Port") ?? 587)
        {
            EnableSsl = true,
            Credentials = new NetworkCredential(configuration["Email:Username"], configuration["Email:Password"]),
            Timeout = 10000
        };
        await client.SendMailAsync(message, cancellationToken);
    }

    private async Task WriteFileAsync(string recipient, string subject, string body,
        CancellationToken cancellationToken)
    {
        var directory = configuration["Email:FileDirectory"]!;
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}.txt");
        await File.WriteAllTextAsync(path, $"To: {recipient}\nSubject: {subject}\n\n{body}",
            cancellationToken);
    }
}

public sealed class EmailOutboxProcessor(ApplicationDbContext db, IChangeEmailSender sender,
    TimeProvider clock, ILogger<EmailOutboxProcessor> logger)
{
    private const int BatchSize = 10;
    private const int MaxAttempts = 5;

    public async Task<int> ProcessAsync(CancellationToken cancellationToken)
    {
        var expiration = clock.GetUtcNow().UtcDateTime.AddDays(-1);
        await db.EmailOutbox.Where(x => x.Status == "pending" && x.NextAttemptAt < expiration)
            .ExecuteUpdateAsync(update => update.SetProperty(x => x.Status, "failed")
                .SetProperty(x => x.LastErrorCode, "expired"), cancellationToken);
        if (!sender.IsConfigured) return 0;
        var processed = 0;
        for (var i = 0; i < BatchSize; i++)
        {
            var claim = await ClaimAsync(cancellationToken);
            if (claim is null) break;
            await DeliverAsync(claim.Value.Id, claim.Value.Token, cancellationToken);
            processed++;
        }
        return processed;
    }

    private async Task<(Guid Id, Guid Token)?> ClaimAsync(CancellationToken cancellationToken)
    {
        db.ChangeTracker.Clear();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var now = clock.GetUtcNow().UtcDateTime;
        await db.EmailOutbox.Where(x => x.Status == "sending" && x.LeaseExpiresAt < now &&
                x.Attempts >= MaxAttempts)
            .ExecuteUpdateAsync(update => update.SetProperty(x => x.Status, "failed")
                .SetProperty(x => x.LastErrorCode, "lease_expired"), cancellationToken);
        var item = await db.EmailOutbox.FromSqlRaw(
            "SELECT * FROM \"EmailOutbox\" WHERE \"Attempts\" < 5 AND ((\"Status\" = 'pending' AND \"NextAttemptAt\" <= {0}) OR (\"Status\" = 'sending' AND \"LeaseExpiresAt\" < {0})) ORDER BY \"NextAttemptAt\" LIMIT 1 FOR UPDATE SKIP LOCKED", now)
            .FirstOrDefaultAsync(cancellationToken);
        if (item is null) return null;
        var token = Guid.NewGuid();
        item.Status = "sending";
        item.Attempts++;
        item.LeaseToken = token;
        item.LeaseExpiresAt = now.AddMinutes(2);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return (item.Id, token);
    }

    private async Task DeliverAsync(Guid id, Guid token, CancellationToken cancellationToken)
    {
        db.ChangeTracker.Clear();
        var item = await db.EmailOutbox.AsNoTracking().Where(x => x.Id == id)
            .Select(x => new { x.Recipient, x.ChangeEventId, x.User.EmailConfirmed,
                CurrentEmail = x.User.Email, x.ChangeEvent.Monitor.EmailAlertsEnabled,
                x.ChangeEvent.Monitor.Name, x.ChangeEvent.Monitor.Url, x.ChangeEvent.DetectedAt })
            .SingleOrDefaultAsync(cancellationToken);
        if (item is null) return;
        string? errorCode = null;
        if (!item.EmailConfirmed || !item.EmailAlertsEnabled || item.CurrentEmail != item.Recipient)
            errorCode = "recipient_ineligible";
        else
        {
            try
            {
                await sender.SendAsync(new ChangeEmail(item.Recipient, item.Name, item.Url,
                    item.ChangeEventId, item.DetectedAt), cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                errorCode = "provider_error";
                logger.LogWarning("Email delivery failed for ChangeEventId {ChangeEventId}: {ErrorType}",
                    item.ChangeEventId, ex.GetType().Name);
            }
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var row = await db.EmailOutbox.FromSqlRaw("SELECT * FROM \"EmailOutbox\" WHERE \"Id\" = {0} FOR UPDATE", id)
            .SingleOrDefaultAsync(cancellationToken);
        if (row is null || row.Status != "sending" || row.LeaseToken != token) return;
        row.LeaseToken = null;
        row.LeaseExpiresAt = null;
        row.LastErrorCode = errorCode;
        if (errorCode is null)
        {
            row.Status = "sent";
            row.SentAt = clock.GetUtcNow().UtcDateTime;
        }
        else if (row.Attempts >= MaxAttempts || errorCode == "recipient_ineligible") row.Status = "failed";
        else
        {
            row.Status = "pending";
            row.NextAttemptAt = clock.GetUtcNow().UtcDateTime.AddMinutes(
                Math.Min(60, (int)Math.Pow(3, row.Attempts - 1)));
        }
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        logger.LogInformation("Email attempt completed: ChangeEventId {ChangeEventId}, Status {Status}, Attempts {Attempts}, ErrorCode {ErrorCode}",
            row.ChangeEventId, row.Status, row.Attempts, row.LastErrorCode);
    }
}
