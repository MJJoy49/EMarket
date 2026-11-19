using EMarket.BLL.AfterSale;
using EMarket.BLL.Common;
using EMarket.BLL.Finance;
using EMarket.Domain.Entities;
using EMarket.Shared.Constants;
using EMarket.Shared.Dtos.AfterSale;
using EMarket.Shared.Dtos.Sellers;
using EMarket.Tests.TestUtils;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace EMarket.Tests.Returns;

public sealed class AfterSaleTests
{
    private readonly Market m = new();

    private static ReturnRequestInput Return(OrderItem item, int quantity, string reason = "Damaged", string method = "StoreCredit") => new()
    {
        Items = [new ReturnLineInput { OrderItemId = item.Id, Quantity = quantity }], Reason = reason, Description = "Screen cracked", RefundMethod = method,
    };

    [Fact]
    public async Task One_of_three_units_on_day_six_is_refunded_and_day_eight_is_too_late()
    {
        var order = await m.PlaceAsync([(m.AddProduct(500), 3)]);
        await m.DeliverAsync(order);
        var item = order.OrderItems.Single();

        m.Clock.UtcNow = m.Clock.UtcNow.AddDays(6);
        var id = await m.Returns.RequestAsync(m.Buyer.Id, order.Id, Return(item, 1, reason: "Other"), []);
        await m.Returns.SellerDecideAsync(m.Seller.Id, id, new DecisionRequest { Approve = true });
        await m.Returns.ItemReceivedAsync(m.Seller.Id, id, resellable: true);

        (await m.Ledger.StoreCreditAsync(m.Buyer.Id)).ShouldBe(500); // COD money goes back as store credit (BR-21); no fee for "Other"
        (await m.Db.Products.SingleAsync()).Stock.ShouldBe(8);
        (await m.LedgerTotalAsync()).ShouldBe(0);

        m.Clock.UtcNow = m.Clock.UtcNow.AddDays(2);
        (await Should.ThrowAsync<AppException>(() => m.Returns.RequestAsync(m.Buyer.Id, order.Id, Return(item, 1), []))).Message.ShouldBe(Messages.ReturnWindowOver);
    }

    [Fact]
    public async Task A_damaged_item_refunds_price_minus_coupon_share_plus_delivery_fee()
    {
        m.Db.Coupons.Add(new Coupon
        {
            Id = Guid.NewGuid(), Code = "SAVE100", DiscountType = "Fixed", DiscountValue = 100, StartsAt = m.Clock.UtcNow.AddDays(-1),
            ExpiresAt = m.Clock.UtcNow.AddDays(1), PerUserLimit = 1, IsActive = true,
        });
        await m.Db.SaveChangesAsync();
        var order = await m.PlaceAsync([(m.AddProduct(1000), 1)], coupon: "SAVE100");

        var amount = ReturnService.RefundAmount(order, [(order.OrderItems.Single(), 1)], "Damaged");

        amount.ShouldBe(1000 - 100 + 60);
    }

    [Fact]
    public async Task A_second_open_request_for_the_same_item_is_refused()
    {
        var order = await m.PlaceAsync([(m.AddProduct(500), 2)]);
        await m.DeliverAsync(order);
        await m.Returns.RequestAsync(m.Buyer.Id, order.Id, Return(order.OrderItems.Single(), 1), []);

        await Should.ThrowAsync<AppException>(() => m.Returns.RequestAsync(m.Buyer.Id, order.Id, Return(order.OrderItems.Single(), 1), []));
    }

    [Fact]
    public async Task Admin_can_overrule_a_seller_rejection_once_the_buyer_asks()
    {
        var order = await m.PlaceAsync([(m.AddProduct(500), 1)]);
        await m.DeliverAsync(order);
        var id = await m.Returns.RequestAsync(m.Buyer.Id, order.Id, Return(order.OrderItems.Single(), 1, "WrongItem"), []);
        await m.Returns.SellerDecideAsync(m.Seller.Id, id, new DecisionRequest { Approve = false, Reason = "The item matches the photos." });

        await m.Returns.EscalateAsync(m.Buyer.Id, id);
        await m.Returns.AdminDecideAsync(m.Admin.Id, id, new DecisionRequest { Approve = true });

        (await m.Db.ReturnRequests.SingleAsync()).Status.ShouldBe("Approved");
        await Should.ThrowAsync<AppException>(() => m.Returns.EscalateAsync(m.Buyer.Id, id));
    }

    [Fact]
    public async Task Not_received_on_day_two_after_shipping_is_refused()
    {
        var order = await m.PlaceAsync([(m.AddProduct(500), 1)]);
        await m.Orders.SellerMoveAsync(m.Seller.Id, order.Id, "Confirmed", null);
        await m.Orders.SellerMoveAsync(m.Seller.Id, order.Id, "Processing", null);
        await m.Orders.ShipAsync(m.Seller.Id, order.Id, new());

        m.Clock.UtcNow = m.Clock.UtcNow.AddDays(2);
        await Should.ThrowAsync<AppException>(() => m.Returns.RequestAsync(m.Buyer.Id, order.Id, Return(order.OrderItems.Single(), 1, "NotReceived"), []));
    }

    [Fact]
    public async Task Reviews_need_delivery_happen_once_and_hiding_changes_the_average()
    {
        var product = m.AddProduct(500);
        var first = await m.PlaceAsync([(product, 1)]);
        await Should.ThrowAsync<AppException>(() => m.Reviews.CreateAsync(m.Buyer.Id, first.OrderItems.Single().Id, new ReviewInput { Rating = 5 }));
        await m.DeliverAsync(first);
        var second = await m.PlaceAsync([(product, 1)]);
        await m.DeliverAsync(second);

        var low = await m.Reviews.CreateAsync(m.Buyer.Id, first.OrderItems.Single().Id, new ReviewInput { Rating = 1 });
        await m.Reviews.CreateAsync(m.Buyer.Id, second.OrderItems.Single().Id, new ReviewInput { Rating = 4 });
        await Should.ThrowAsync<AppException>(() => m.Reviews.CreateAsync(m.Buyer.Id, first.OrderItems.Single().Id, new ReviewInput { Rating = 5 }));
        (await m.Db.Products.SingleAsync()).AverageRating.ShouldBe(2.5m);

        await m.Reviews.SetHiddenAsync(m.Admin.Id, low, true, "Abusive language in the comment");

        (await m.Db.Products.SingleAsync()).AverageRating.ShouldBe(4m);
        ReviewService.Mask("Abdul Karim").ShouldBe("A*** K***");
    }

    [Fact]
    public async Task Earnings_become_available_after_the_window_and_payouts_follow_the_ledger()
    {
        var order = await m.PlaceAsync([(m.AddProduct(1000), 1)]);
        await m.DeliverAsync(order);
        (await m.Earnings.BalancesAsync(m.Shop.Id)).Pending.ShouldBe(960);

        m.Clock.UtcNow = m.Clock.UtcNow.AddDays(8);
        (await m.Earnings.ReleaseDueAsync()).ShouldBe(1);
        (await m.Earnings.ReleaseDueAsync()).ShouldBe(0);
        (await m.Earnings.BalancesAsync(m.Shop.Id)).Available.ShouldBe(960);

        await Should.ThrowAsync<AppException>(() => m.Earnings.RequestPayoutAsync(m.Seller.Id, 400)); // below Tk 500
        (await Should.ThrowAsync<AppException>(() => m.Earnings.RequestPayoutAsync(m.Seller.Id, 1000))).Message.ShouldBe(Messages.OverBalance);
        var rejected = await m.Earnings.RequestPayoutAsync(m.Seller.Id, 600);
        await Should.ThrowAsync<AppException>(() => m.Earnings.RequestPayoutAsync(m.Seller.Id, 300)); // one open request
        await m.Earnings.RejectPayoutAsync(m.Admin.Id, rejected, "Account number does not match the NID");
        (await m.Earnings.BalancesAsync(m.Shop.Id)).Available.ShouldBe(960);

        var paid = await m.Earnings.RequestPayoutAsync(m.Seller.Id, 900);
        await m.Earnings.PayAsync(m.Admin.Id, paid, new PayPayoutRequest { ReferenceNo = "BK-778899" });

        var balances = await m.Earnings.BalancesAsync(m.Shop.Id);
        (balances.Available, balances.Reserved, balances.PaidOut).ShouldBe((60m, 0m, 900m));
        (await m.LedgerTotalAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task A_refund_after_payout_makes_the_balance_negative_and_blocks_payouts()
    {
        var order = await m.PlaceAsync([(m.AddProduct(1000), 1)]);
        await m.DeliverAsync(order);
        m.Clock.UtcNow = m.Clock.UtcNow.AddDays(8);
        await m.Earnings.ReleaseDueAsync();
        await m.Earnings.PayAsync(m.Admin.Id, await m.Earnings.RequestPayoutAsync(m.Seller.Id, 960), new PayPayoutRequest { ReferenceNo = "BK-1" });

        // The admin later approves a not-received report within 14 days of delivery.
        var id = await m.Returns.RequestAsync(m.Buyer.Id, order.Id, Return(order.OrderItems.Single(), 1, "NotReceived"), []);
        await m.Returns.AdminDecideAsync(m.Admin.Id, id, new DecisionRequest { Approve = true });
        await m.Refunds.ProcessPendingAsync();

        (await m.Earnings.BalancesAsync(m.Shop.Id)).Available.ShouldBe(-960);
        await Should.ThrowAsync<AppException>(() => m.Earnings.RequestPayoutAsync(m.Seller.Id, 500));
        (await m.LedgerTotalAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task The_invoice_is_a_pdf_only_for_the_owner_and_only_after_confirmation()
    {
        var order = await m.PlaceAsync([(m.AddProduct(500), 1)]);
        var invoices = new EMarket.BLL.Orders.InvoiceService(m.Db);
        await Should.ThrowAsync<AppException>(() => invoices.BuildAsync(m.Buyer.Id, order.Id)); // still Pending

        await m.Orders.SellerMoveAsync(m.Seller.Id, order.Id, "Confirmed", null);
        var (pdf, name) = await invoices.BuildAsync(m.Buyer.Id, order.Id);

        System.Text.Encoding.ASCII.GetString(pdf, 0, 4).ShouldBe("%PDF");
        name.ShouldStartWith("invoice-EM-");
        (await Should.ThrowAsync<AppException>(() => invoices.BuildAsync(m.Seller.Id, order.Id))).StatusCode.ShouldBe(404); // not the buyer
    }

    [Fact]
    public async Task Reorder_copies_available_items_and_names_the_skipped_ones()
    {
        var kept = m.AddProduct(500);
        var gone = m.AddProduct(700);
        var order = await m.PlaceAsync([(kept, 1), (gone, 1)]);
        gone.Status = "Hidden";
        await m.Db.SaveChangesAsync();

        var skipped = await m.Carts.ReorderAsync(m.Buyer.Id, order.Id);

        skipped.ShouldBe([gone.Name]);
        (await m.Db.CartItems.SingleAsync()).ProductId.ShouldBe(kept.Id);
    }

    [Fact]
    public async Task Changing_payout_details_needs_the_password()
    {
        var request = new PayoutDetailsRequest { CurrentPassword = "Wrong7Pass", PayoutMethod = "Nagad", WalletNumber = "01812345678" };
        await Should.ThrowAsync<AppException>(() => m.Earnings.UpdatePayoutDetailsAsync(m.Seller.Id, request));

        request.CurrentPassword = "Tiger7Lamp";
        await m.Earnings.UpdatePayoutDetailsAsync(m.Seller.Id, request);
        (await m.Db.Shops.SingleAsync()).PayoutMethod.ShouldBe("Nagad");
    }
}
