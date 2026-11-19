using EMarket.BLL.Admin;
using EMarket.BLL.AfterSale;
using EMarket.BLL.Catalog;
using EMarket.BLL.Common;
using EMarket.DAL;
using EMarket.BLL.Finance;
using EMarket.BLL.Shopping;
using EMarket.BLL.Identity;
using EMarket.BLL.Messaging;
using EMarket.BLL.Orders;
using EMarket.BLL.Payments;
using EMarket.BLL.Profile;
using EMarket.BLL.Sellers;
using EMarket.Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace EMarket.BLL;

public static class BllModule
{
    /// <summary>Wires the business layer and, below it, the data layer. The API host never touches the DAL (NFR-68).</summary>
    public static IServiceCollection AddBllModule(this IServiceCollection services, string connectionString)
    {
        services.AddDalModule(connectionString);
        services.AddMemoryCache();
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IPasswordHasher<User>, PasswordHasher<User>>();
        services.AddScoped<ISettingsReader, SettingsReader>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<AdminSeeder>();
        services.AddSingleton<IFileStorage, LocalFileStorage>();
        services.AddSingleton<ISecretProtector, SecretProtector>();
        services.AddScoped<Notifier>();
        services.AddScoped<ProfileService>();
        services.AddScoped<AddressService>();
        services.AddScoped<SellerApplicationService>();
        services.AddScoped<CategoryService>();
        services.AddScoped<ProductService>();
        services.AddScoped<CatalogQueryService>();
        services.AddScoped<Ledger>();
        services.AddScoped<CartService>();
        services.AddScoped<CheckoutService>();
        services.AddScoped<OrderService>();
        services.AddScoped<InvoiceService>();
        services.AddScoped<PaymentService>();
        services.AddScoped<RefundService>();
        services.AddScoped<ReturnService>();
        services.AddScoped<ReviewService>();
        services.AddScoped<NotificationService>();
        services.AddScoped<EarningsService>();
        services.AddScoped<UserAdminService>();
        services.AddScoped<ContentAdminService>();
        services.AddScoped<ReportService>();
        services.AddScoped<ReportsGuard>();
        services.AddScoped<OutboxProcessor>();
        services.AddScoped<ScheduledJobs>();
        services.AddSingleton<IEmailSender, SmtpEmailSender>();
        return services;
    }
}
