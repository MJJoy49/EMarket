using EMarket.BLL.Common;
using EMarket.BLL.Sellers;
using EMarket.DAL.Data;
using EMarket.Shared.Constants;
using EMarket.Shared.Dtos.Sellers;
using EMarket.Tests.TestUtils;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace EMarket.Tests.Sellers;

public sealed class SellerApplicationTests
{
    private readonly AppDbContext db = TestDb.Create();
    private readonly FakeClock clock = new();
    private readonly SellerApplicationService service;

    public SellerApplicationTests() =>
        service = new SellerApplicationService(db, new FakeFiles(), new FakeSecrets(), new Notifier(db, clock), clock);

    private static ApplicationFiles Files() => new(Photo(), Photo(), null);

    private static UploadFile Photo() => new(new MemoryStream([0xFF, 0xD8, 0xFF, 0x00]), 4);

    private static SellerApplicationRequest Request(string shop = "Rahim Store") => new()
    {
        ShopName = shop,
        BusinessType = "Individual",
        NidNumber = "1234567890",
        PayoutMethod = "bKash",
        WalletNumber = "01712345678",
        AcceptSellerTerms = true,
    };

    [Fact]
    public async Task Unverified_buyer_cannot_apply()
    {
        var user = db.AddUser(emailConfirmed: false);

        var error = await Should.ThrowAsync<AppException>(() => service.ApplyAsync(user.Id, Request(), Files()));

        error.Message.ShouldBe(Messages.VerifyEmailFirst);
    }

    [Fact]
    public async Task Application_is_pending_encrypted_and_admins_are_told()
    {
        var admin = db.AddUser(Roles.Admin);
        var user = db.AddUser();

        await service.ApplyAsync(user.Id, Request(), Files());

        var saved = await db.SellerApplications.SingleAsync();
        saved.Status.ShouldBe("Pending");
        saved.NidNumber.ShouldNotBe("1234567890");
        (await db.Notifications.AnyAsync(n => n.UserId == admin.Id)).ShouldBeTrue();
    }

    [Fact]
    public async Task Shop_name_is_unique_without_regard_to_case()
    {
        await service.ApplyAsync(db.AddUser().Id, Request("Rahim Store"), Files());

        var error = await Should.ThrowAsync<AppException>(() => service.ApplyAsync(db.AddUser().Id, Request("RAHIM STORE"), Files()));

        error.Message.ShouldBe(Messages.Conflict);
    }

    [Fact]
    public async Task Bank_payout_needs_a_routing_number()
    {
        var request = Request();
        request.PayoutMethod = "BankTransfer";
        request.BankName = "Islami Bank";
        request.BankBranch = "Dhanmondi";
        request.AccountName = "Rahim";
        request.AccountNumber = "1234567";

        await Should.ThrowAsync<AppException>(() => service.ApplyAsync(db.AddUser().Id, request, Files()));
    }

    [Fact]
    public async Task Approval_adds_the_seller_role_and_creates_an_active_shop()
    {
        var admin = db.AddUser(Roles.Admin);
        var user = db.AddUser();
        var id = await service.ApplyAsync(user.Id, Request(), Files());

        await service.DecideAsync(admin.Id, id, new ReviewDecisionRequest { Approve = true });

        var roles = await db.Users.Where(u => u.Id == user.Id).SelectMany(u => u.Roles.Select(r => r.Name)).ToListAsync();
        roles.ShouldBe([Roles.Buyer, Roles.Seller], ignoreOrder: true);
        var shop = await db.Shops.SingleAsync();
        (shop.Status, shop.Slug, shop.InsideDhakaFee, shop.FreeDeliveryAbove).ShouldBe(("Active", "rahim-store", 60m, 2000m));
    }

    [Fact]
    public async Task Rejection_needs_a_reason_and_reapplying_waits_seven_days()
    {
        var admin = db.AddUser(Roles.Admin);
        var user = db.AddUser();
        var id = await service.ApplyAsync(user.Id, Request(), Files());

        await Should.ThrowAsync<AppException>(() => service.DecideAsync(admin.Id, id, new ReviewDecisionRequest { Approve = false }));
        await service.DecideAsync(admin.Id, id, new ReviewDecisionRequest { Approve = false, Reason = "Please upload a clear NID photo" });

        clock.UtcNow = clock.UtcNow.AddDays(3);
        await Should.ThrowAsync<AppException>(() => service.ApplyAsync(user.Id, Request(), Files()));

        clock.UtcNow = clock.UtcNow.AddDays(5);
        await service.ApplyAsync(user.Id, Request(), Files());
    }

    [Fact]
    public async Task Opening_a_document_is_logged()
    {
        var admin = db.AddUser(Roles.Admin);
        var id = await service.ApplyAsync(db.AddUser().Id, Request(), Files());

        await using var stream = await service.OpenDocumentAsync(admin.Id, id, "nid-front");

        (await db.AuditLogs.CountAsync(a => a.Action == "DocumentOpened" && a.UserId == admin.Id)).ShouldBe(1);
    }
}
