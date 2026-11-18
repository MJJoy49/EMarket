using EMarket.BLL.Common;
using EMarket.BLL.Identity;
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
        return services;
    }
}
