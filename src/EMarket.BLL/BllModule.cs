using EMarket.BLL.Catalog;
using EMarket.BLL.Common;
using EMarket.BLL.Identity;
using EMarket.BLL.Profile;
using EMarket.BLL.Sellers;
using EMarket.Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace EMarket.BLL;

public static class BllModule
{
    public static IServiceCollection AddBllModule(this IServiceCollection services)
    {
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
        return services;
    }
}
