using System.Text.Json;
using EMarket.BLL.Common;
using EMarket.BLL.Finance;
using EMarket.BLL.Orders;
using EMarket.BLL.Payments;
using EMarket.DAL.Data;
using EMarket.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MimeKit;

namespace EMarket.BLL.Messaging;

public interface IEmailSender
{
    Task SendAsync(string to, string subject, string body, CancellationToken ct);
}

/// <summary>SMTP through MailKit (MailHog on a development machine). Secrets come from user-secrets or environment variables (NFR-42).</summary>
public sealed class SmtpEmailSender(IConfiguration config) : IEmailSender
{
    public async Task SendAsync(string to, string subject, string body, CancellationToken ct)
    {
        var message = new MimeMessage();
        message.From.Add(MailboxAddress.Parse(config["Smtp:From"] ?? "no-reply@emarket.local"));
        message.To.Add(MailboxAddress.Parse(to));
        message.Subject = subject;
        message.Body = new TextPart("plain") { Text = body };

        using var client = new MailKit.Net.Smtp.SmtpClient { Timeout = 10_000 }; // NFR-7: 10 second timeout
        var secure = bool.TryParse(config["Smtp:UseTls"], out var tls) && tls;
        await client.ConnectAsync(config["Smtp:Host"] ?? "localhost", int.TryParse(config["Smtp:Port"], out var port) ? port : 1025,
            secure ? MailKit.Security.SecureSocketOptions.StartTls : MailKit.Security.SecureSocketOptions.None, ct);
        if (!string.IsNullOrEmpty(config["Smtp:User"]))
        {
            await client.AuthenticateAsync(config["Smtp:User"]!, config["Smtp:Password"] ?? "", ct);
        }

        await client.SendAsync(message, ct);
        await client.DisconnectAsync(true, ct);
    }
}

/// <summary>
/// AD-9 and FR-14.5: events are saved with the business change; this job sends them later. A failed email is retried
/// three times with a growing delay, is logged, and never stops the business action.
/// </summary>
public sealed class OutboxProcessor(AppDbContext db, IEmailSender mail, IClock clock, ILogger<OutboxProcessor> logger)
{
    public const int MaxAttempts = 3;

    public async Task<int> ProcessAsync(CancellationToken ct = default)
    {
        var now = clock.UtcNow;
        var due = await db.OutboxMessages.Where(m => m.Status == "Pending" && m.NextAttemptAt <= now)
            .OrderBy(m => m.NextAttemptAt).Take(100).ToListAsync(ct);
        foreach (var message in due)
        {
            try
            {
                if (message.Type == "Email")
                {
                    var email = JsonSerializer.Deserialize<EmailPayload>(message.Payload)!;
                    await mail.SendAsync(email.To, email.Subject, email.Body, ct);
                }

                message.Status = "Done";
                message.ProcessedAt = clock.UtcNow;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                message.Attempts++;
                message.LastError = ex.Message[..Math.Min(500, ex.Message.Length)];
                message.NextAttemptAt = clock.UtcNow.AddMinutes(Math.Pow(5, message.Attempts)); // 5, 25 minutes
                message.Status = message.Attempts >= MaxAttempts ? "Failed" : "Pending";
                logger.LogWarning("Outbox message {Id} failed (attempt {Attempts})", message.Id, message.Attempts);
            }
        }

        await db.SaveChangesAsync(ct);
        return due.Count;
    }

    private sealed record EmailPayload(string To, string Subject, string Body);
}

/// <summary>The timed rules of the SRS, run by Hangfire (AD-8). Each one is safe to run twice.</summary>
public sealed class ScheduledJobs(
    AppDbContext db, PaymentService payments, RefundService refunds, EarningsService earnings, OrderService orders, ReportsGuard guard,
    Notifier notifier, ISettingsReader settings, IClock clock, ILogger<ScheduledJobs> logger)
{
    public Task ExpireUnpaidOrdersAsync() => payments.ExpireUnpaidAsync();

    public Task ReconcilePaymentsAsync() => payments.ReconcileAsync();

    public Task ProcessRefundsAsync() => refunds.ProcessPendingAsync();

    public Task ReleaseEarningsAsync() => earnings.ReleaseDueAsync();

    /// <summary>FR-11.9: a Pending order the seller has not confirmed in 48 hours (setting) is cancelled and refunded.</summary>
    public async Task CancelUnconfirmedOrdersAsync()
    {
        var hours = await settings.GetIntAsync("sellerConfirmHours", 48);
        var cutoff = clock.UtcNow.AddHours(-hours);
        var ids = await db.Orders.Where(o => o.Status == OrderRules.Pending && o.UpdatedAt <= cutoff).Select(o => o.Id).Take(100).ToListAsync();
        foreach (var id in ids)
        {
            var order = await orders.ChangeQuery().FirstAsync(o => o.Id == id);
            await orders.ChangeStatusAsync(order, OrderRules.Cancelled, Actor.System, null, "The seller did not confirm in time", CancellationToken.None);
            await db.SaveChangesAsync();
        }

        await refunds.ProcessPendingAsync();
    }

    /// <summary>The retention periods of SRS 6.6 (NFR-77).</summary>
    public async Task CleanUpAsync()
    {
        var now = clock.UtcNow;
        await db.Notifications.Where(n => n.CreatedAt < now.AddDays(-90)).ExecuteDeleteAsync(); // BR-39
        await db.IdempotencyKeys.Where(k => k.ExpiresAt < now).ExecuteDeleteAsync();
        await db.EmailTokens.Where(t => t.ExpiresAt < now.AddDays(-7)).ExecuteDeleteAsync();
        await db.RefreshTokens.Where(t => t.ExpiresAt < now.AddDays(-7) && !db.RefreshTokens.Any(x => x.ReplacedByTokenId == t.Id && x.ExpiresAt >= now.AddDays(-7)))
            .ExecuteDeleteAsync();
        await db.OutboxMessages.Where(m => m.Status == "Done" && m.ProcessedAt < now.AddDays(-30)).ExecuteDeleteAsync();
    }

    /// <summary>FR-24.8: every ledger transaction must add up to 0. A difference is logged and the admins are told.</summary>
    public async Task CheckLedgerAsync()
    {
        var bad = await guard.UnbalancedAsync();
        if (bad.Count > 0)
        {
            logger.LogError("Ledger check found {Count} unbalanced transactions", bad.Count);
            await notifier.NotifyAdminsAsync("ReconciliationDifference", "Ledger difference found", $"{bad.Count} transaction(s) do not balance.", "/admin/payments");
            await db.SaveChangesAsync();
        }
    }
}

/// <summary>A thin wrapper so the job class does not depend on the whole report service.</summary>
public sealed class ReportsGuard(AppDbContext db)
{
    public async Task<IReadOnlyList<Guid>> UnbalancedAsync() =>
        await db.LedgerEntries.GroupBy(e => e.TransactionId).Where(g => g.Sum(e => e.Amount) != 0).Select(g => g.Key).Take(100).ToListAsync();
}
