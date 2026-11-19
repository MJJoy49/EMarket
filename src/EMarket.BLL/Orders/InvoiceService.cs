using EMarket.BLL.Common;
using EMarket.DAL.Data;
using EMarket.Shared.Constants;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace EMarket.BLL.Orders;

/// <summary>FR-10.8 and BR-42: a PDF invoice from Confirmed onward, for the buyer who owns the order only.</summary>
public sealed class InvoiceService(AppDbContext db)
{
    private static readonly string[] AllowedStatuses = [OrderRules.Confirmed, OrderRules.Processing, OrderRules.Shipped, OrderRules.Delivered];

    static InvoiceService() => QuestPDF.Settings.License = LicenseType.Community; // the community license named in SRS 3.3

    public async Task<(byte[] Pdf, string FileName)> BuildAsync(Guid buyerId, Guid orderId, CancellationToken ct = default)
    {
        var order = await db.Orders.AsNoTracking().Include(o => o.Shop).Include(o => o.Buyer).Include(o => o.OrderItems)
            .FirstOrDefaultAsync(o => o.Id == orderId && o.BuyerId == buyerId, ct)
            ?? throw new AppException(404, Messages.NotFound); // another user's order answers like a missing one (FR-10.10)
        if (!AllowedStatuses.Contains(order.Status))
        {
            throw new AppException(409, Messages.Conflict);
        }

        var siteName = (await db.SystemSettings.AsNoTracking().Where(s => s.Key == "siteName").Select(s => s.Value).FirstOrDefaultAsync(ct)) ?? "EMarket";
        var bangladesh = TimeSpan.FromHours(6);
        string Tk(decimal v) => $"Tk {v:N2}";

        var pdf = Document.Create(document => document.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(36);
            page.DefaultTextStyle(x => x.FontSize(10));
            page.Header().Row(row =>
            {
                row.RelativeItem().Text(siteName).FontSize(20).Bold();
                row.RelativeItem().AlignRight().Column(c =>
                {
                    c.Item().Text("INVOICE").FontSize(16).Bold();
                    c.Item().Text($"Order {order.OrderNumber}");
                    c.Item().Text($"Date {order.CreatedAt.Add(bangladesh):dd/MM/yyyy}");
                });
            });
            page.Content().PaddingVertical(16).Column(column =>
            {
                column.Spacing(12);
                column.Item().Row(row =>
                {
                    row.RelativeItem().Column(c =>
                    {
                        c.Item().Text("Sold by").Bold();
                        c.Item().Text(order.Shop.Name);
                    });
                    row.RelativeItem().Column(c =>
                    {
                        c.Item().Text("Deliver to").Bold();
                        c.Item().Text(order.ShipName);
                        c.Item().Text(order.ShipAddress);
                        c.Item().Text(order.ShipPhone);
                    });
                });
                column.Item().Table(table =>
                {
                    table.ColumnsDefinition(c =>
                    {
                        c.RelativeColumn(5);
                        c.RelativeColumn(1);
                        c.RelativeColumn(2);
                        c.RelativeColumn(2);
                    });
                    table.Header(h =>
                    {
                        foreach (var title in new[] { "Item", "Qty", "Unit price", "Total" })
                        {
                            h.Cell().BorderBottom(1).PaddingVertical(4).Text(title).Bold();
                        }
                    });
                    foreach (var item in order.OrderItems)
                    {
                        table.Cell().PaddingVertical(3).Text($"{item.ProductName}{(string.IsNullOrEmpty(item.VariantText) ? "" : $" ({item.VariantText})")}");
                        table.Cell().PaddingVertical(3).Text(item.Quantity.ToString());
                        table.Cell().PaddingVertical(3).Text(Tk(item.UnitPrice));
                        table.Cell().PaddingVertical(3).Text(Tk(item.UnitPrice * item.Quantity));
                    }
                });
                column.Item().AlignRight().Column(c =>
                {
                    c.Item().Text($"Items: {Tk(order.ItemsTotal)}");
                    c.Item().Text($"Delivery: {Tk(order.DeliveryFee)}");
                    if (order.DiscountAmount > 0) { c.Item().Text($"Coupon discount: -{Tk(order.DiscountAmount)}"); }
                    if (order.CreditUsed > 0) { c.Item().Text($"Store credit used: -{Tk(order.CreditUsed)}"); }
                    c.Item().Text($"Total paid or payable: {Tk(order.TotalAmount)}").Bold().FontSize(12);
                });
            });
            page.Footer().AlignCenter().Text("Thank you for shopping with " + siteName).FontSize(8);
        })).GeneratePdf();
        return (pdf, $"invoice-{order.OrderNumber}.pdf");
    }
}
