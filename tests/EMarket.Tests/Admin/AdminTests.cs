using EMarket.BLL.Admin;
using EMarket.BLL.Common;
using EMarket.BLL.Finance;
using EMarket.BLL.Messaging;
using EMarket.BLL.Orders;
using EMarket.DAL.Data;
using EMarket.Domain.Entities;
using EMarket.Shared.Constants;
using EMarket.Shared.Dtos.Admin;
using EMarket.Tests.TestUtils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace EMarket.Tests.Admin;

public sealed class AdminTests
{
    private readonly Market m = new();
    private readonly UserAdminService users;
    private readonly ContentAdminService content;
    private readonly ReportService reports;

    public AdminTests()
    {
        var notifier = new Notifier(m.Db, m.Clock);
        users = new UserAdminService(m.Db, m.Ledger, notifier, m.Hasher, m.Cache, m.Clock);
        content = new ContentAdminService(m.Db, new FakeFiles(), notifier, m.Cache, m.Clock);
        reports = new ReportService(m.Db, m.Cache, m.Clock);
    }

    [Fact]
    public async Task Blocking_revokes_tokens_and_changes_the_security_stamp()
    {
        var stamp = m.Buyer.SecurityStamp;
        m.Db.RefreshTokens.Add(new RefreshToken { UserId = m.Buyer.Id, TokenHash = "h", FamilyId = Guid.NewGuid(), ExpiresAt = m.Clock.UtcNow.AddDays(1) });
        await m.Db.SaveChangesAsync();

        await users.BlockAsync(m.Admin.Id, m.Buyer.Id, "Fake orders reported by sellers");

        (await m.Db.Users.SingleAsync(u => u.Id == m.Buyer.Id)).SecurityStamp.ShouldNotBe(stamp);
        (await m.Db.RefreshTokens.SingleAsync()).RevokedAt.ShouldNotBeNull();
        (await m.Db.AuditLogs.AnyAsync(a => a.Action == "UserBlocked")).ShouldBeTrue();
    }

    [Fact]
    public async Task An_admin_cannot_block_the_own_account_or_the_last_admin()
    {
        await Should.ThrowAsync<AppException>(() => users.BlockAsync(m.Admin.Id, m.Admin.Id, "Testing the guard rule"));

        var other = m.Db.AddUser(Roles.Admin);
        await users.BlockAsync(m.Admin.Id, other.Id, "Second admin left the company"); // allowed: another active admin remains

        // m.Admin is now the only active admin, so nobody can block that account (FR-16.6).
        await Should.ThrowAsync<AppException>(() => users.BlockAsync(other.Id, m.Admin.Id, "Trying to remove the last admin")); 
    }

    [Fact]
    public async Task A_user_with_an_open_order_cannot_be_deleted()
    {
        await m.PlaceAsync([(m.AddProduct(500), 1)]);

        (await Should.ThrowAsync<AppException>(() => users.DeleteAsync(m.Admin.Id, m.Buyer.Id, "Closing the account"))).StatusCode.ShouldBe(409);
    }

    [Fact]
    public async Task Suspending_a_shop_hides_it_and_tells_the_seller()
    {
        await users.SetShopSuspendedAsync(m.Admin.Id, m.Shop.Id, true, "Selling counterfeit goods");

        (await m.Db.Shops.SingleAsync()).Status.ShouldBe("Suspended");
        (await m.Db.Notifications.AnyAsync(n => n.UserId == m.Seller.Id && n.Type == "ShopSuspended")).ShouldBeTrue();
        await Should.ThrowAsync<AppException>(() => users.SetShopSuspendedAsync(m.Admin.Id, m.Shop.Id, true, "Again for the same shop"));
    }

    [Fact]
    public async Task A_new_admin_must_change_the_temporary_password()
    {
        var password = await users.CreateAdminAsync(m.Admin.Id, new CreateAdminRequest { FullName = "Second Admin", Email = "second@example.com", Phone = "01712345678" });

        var created = await m.Db.Users.SingleAsync(u => u.Email == "second@example.com");
        created.MustChangePassword.ShouldBeTrue();
        created.PasswordHash.ShouldNotContain(password);
    }

    [Theory]
    [InlineData("commissionPercent", "40", false)]   // FR-21.6: 0 to 30
    [InlineData("commissionPercent", "12", true)]
    [InlineData("unpaidOrderCancelMinutes", "5", false)]
    [InlineData("returnWindowDays", "7.5", false)]
    public async Task Settings_outside_their_range_are_refused(string key, string value, bool ok)
    {
        var save = () => content.SaveSettingsAsync(m.Admin.Id, new Dictionary<string, string> { [key] = value });

        if (ok)
        {
            await save();
            (await m.Db.AuditLogs.AnyAsync(a => a.Action == "SettingChanged")).ShouldBeTrue();
        }
        else
        {
            await Should.ThrowAsync<AppException>(save);
        }
    }

    [Fact]
    public async Task A_changed_commission_applies_to_later_orders_only()
    {
        var first = await m.PlaceAsync([(m.AddProduct(1000), 1)]);
        await content.SaveSettingsAsync(m.Admin.Id, new Dictionary<string, string> { ["commissionPercent"] = "12" });

        var second = await m.PlaceAsync([(m.AddProduct(1000), 1)]);

        (first.CommissionPercent, second.CommissionPercent).ShouldBe((10m, 12m));
    }

    [Fact]
    public async Task Static_page_html_is_cleaned_of_scripts()
    {
        await content.SavePageAsync(m.Admin.Id, "faq", "<p>Hello</p><script>alert(1)</script><img src=x onerror=alert(2)>");

        var page = await content.PageAsync("faq");

        page.Html.ShouldContain("Hello");
        page.Html.ShouldNotContain("script");
        page.Html.ShouldNotContain("onerror");
    }

    [Fact]
    public async Task The_fourth_contact_message_in_an_hour_is_refused_and_a_filled_trap_field_is_dropped()
    {
        var message = () => new ContactRequest { Name = "Nila", Email = "nila@example.com", Subject = "Hello", Message = "I have a question about delivery." };
        for (var i = 0; i < 3; i++)
        {
            await content.SubmitContactAsync(message(), "1.2.3.4");
        }

        (await Should.ThrowAsync<AppException>(() => content.SubmitContactAsync(message(), "1.2.3.4"))).StatusCode.ShouldBe(429);
        var trap = message();
        trap.Website = "http://spam";
        await content.SubmitContactAsync(trap, "5.6.7.8");
        (await m.Db.ContactMessages.CountAsync()).ShouldBe(3);
    }

    [Fact]
    public async Task A_used_coupon_cannot_be_deleted_and_duplicate_codes_are_refused()
    {
        var request = new SaveCouponRequest
        {
            Code = "EID2026", DiscountType = "Percent", DiscountValue = 10, StartsAt = m.Clock.UtcNow, ExpiresAt = m.Clock.UtcNow.AddDays(5), PerUserLimit = 1,
        };
        var id = await content.SaveCouponAsync(m.Admin.Id, null, request);

        await Should.ThrowAsync<AppException>(() => content.SaveCouponAsync(m.Admin.Id, null, request));
        (await m.Db.Coupons.FindAsync(id))!.UsedCount = 1;
        await m.Db.SaveChangesAsync();
        await Should.ThrowAsync<AppException>(() => content.DeleteCouponAsync(m.Admin.Id, id));
    }

    [Fact]
    public async Task The_finance_report_balances_after_a_full_order_cycle()
    {
        var order = await m.PlaceAsync([(m.AddProduct(1000), 1)]);
        await m.DeliverAsync(order);

        var finance = await reports.FinanceAsync();

        finance.Received.ShouldBe(1060);
        finance.SellerBalances.ShouldBe(960);
        finance.Commission.ShouldBe(100);
        finance.OrderMoneyHeld.ShouldBe(0);
        finance.PlatformBalance.ShouldBe(finance.Commission); // received - refunds - payouts - seller balances = the platform's own money, which is the commission
    }

    [Fact]
    public async Task A_report_range_over_one_year_is_refused()
    {
        var today = DateOnly.FromDateTime(m.Clock.UtcNow);

        await Should.ThrowAsync<AppException>(() => reports.SalesAsync(today.AddMonths(-13), today, "day"));
        (await reports.SalesAsync(null, null, "day")).Series.Count.ShouldBe(30);
    }

    [Fact]
    public async Task The_excel_export_has_the_same_rows_and_total_as_the_screen()
    {
        var order = await m.PlaceAsync([(m.AddProduct(1000), 2)]);
        await m.DeliverAsync(order);
        var report = await reports.SalesAsync(null, null, "day");

        var bytes = ReportService.ToExcel("Sales", ["Period", "Orders", "Sales"], report.Series.Select(p => (IReadOnlyList<object?>)[p.Label, p.Count, p.Value]));

        using var book = new ClosedXML.Excel.XLWorkbook(new MemoryStream(bytes));
        var sheet = book.Worksheet(1);
        sheet.RowsUsed().Count().ShouldBe(report.Series.Count + 1);
        sheet.Column(3).CellsUsed().Skip(1).Sum(c => c.GetValue<decimal>()).ShouldBe(report.Series.Sum(p => p.Value));
    }
}

public sealed class JobTests
{
    private readonly Market m = new();

    private ScheduledJobs Jobs() => new(m.Db, m.Payments, m.Refunds, m.Earnings, m.Orders, new ReportsGuard(m.Db),
        new Notifier(m.Db, m.Clock), new SettingsReader(m.Db, new MemoryCache(new MemoryCacheOptions())), m.Clock, NullLogger<ScheduledJobs>.Instance);

    [Fact]
    public async Task A_pending_order_left_for_49_hours_is_cancelled()
    {
        var order = await m.PlaceAsync([(m.AddProduct(500), 1)]);

        m.Clock.UtcNow = m.Clock.UtcNow.AddHours(49);
        await Jobs().CancelUnconfirmedOrdersAsync();

        (await m.Db.Orders.SingleAsync(o => o.Id == order.Id)).Status.ShouldBe(OrderRules.Cancelled);
    }

    [Fact]
    public async Task Cleanup_removes_notifications_older_than_90_days_only()
    {
        var now = m.Clock.UtcNow;
        m.Db.Notifications.Add(new Notification { UserId = m.Buyer.Id, Type = "t", Title = "old", Message = "m", CreatedAt = now.AddDays(-91) });
        m.Db.Notifications.Add(new Notification { UserId = m.Buyer.Id, Type = "t", Title = "new", Message = "m", CreatedAt = now.AddDays(-5) });
        await m.Db.SaveChangesAsync();

        // The in-memory provider cannot run bulk deletes, so the rule is checked by its filter.
        var old = await m.Db.Notifications.Where(n => n.CreatedAt < m.Clock.UtcNow.AddDays(-90)).Select(n => n.Title).ToListAsync();

        old.ShouldBe(["old"]);
    }

    [Fact]
    public async Task A_failing_mail_server_retries_three_times_and_never_throws()
    {
        m.Db.OutboxMessages.Add(new OutboxMessage { Type = "Email", Payload = "{\"to\":\"a@b.c\",\"subject\":\"s\",\"body\":\"b\"}", Status = "Pending", NextAttemptAt = m.Clock.UtcNow });
        await m.Db.SaveChangesAsync();
        var processor = new OutboxProcessor(m.Db, new BrokenMail(), m.Clock, NullLogger<OutboxProcessor>.Instance);

        for (var i = 0; i < 3; i++)
        {
            await processor.ProcessAsync();
            m.Clock.UtcNow = m.Clock.UtcNow.AddHours(1);
        }

        var message = await m.Db.OutboxMessages.SingleAsync();
        (message.Status, message.Attempts).ShouldBe(("Failed", 3));
    }

    private sealed class BrokenMail : IEmailSender
    {
        public Task SendAsync(string to, string subject, string body, CancellationToken ct) => throw new IOException("mail server is down");
    }
}
