using System.Threading.RateLimiting;
using EMarket.Api.Infrastructure;
using EMarket.BLL;
using EMarket.BLL.Identity;
using EMarket.BLL.Payments;
using EMarket.DAL;
using EMarket.Shared.Constants;
using EMarket.Shared.Results;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, config) => config
    .ReadFrom.Configuration(context.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console());

var jwt = builder.Configuration.GetSection("Jwt").Get<JwtOptions>() ?? new JwtOptions();
builder.Services.AddSingleton(jwt);
builder.Services.AddSingleton<IAccessTokenIssuer, JwtAccessTokenIssuer>();

builder.Services.AddDalModule(builder.Configuration.GetConnectionString("DefaultConnection")!);
builder.Services.AddBllModule();
var keysFolder = builder.Configuration["DataProtection:KeysFolder"];
var dataProtection = builder.Services.AddDataProtection().SetApplicationName("EMarket");
if (!string.IsNullOrWhiteSpace(keysFolder))
{
    dataProtection.PersistKeysToFileSystem(new DirectoryInfo(keysFolder));
}
// AD-7: the gateway is an outside party. Session start and refunds are not retried blindly; the stand-in is used without credentials.
var gateway = GatewayOptions.From(builder.Configuration);
builder.Services.AddSingleton(gateway);
if (gateway.Mode == "SslCommerz")
{
    builder.Services.AddHttpClient<IPaymentGateway, SslCommerzGateway>(client => client.Timeout = TimeSpan.FromSeconds(10));
}
else
{
    builder.Services.AddSingleton<IPaymentGateway, FakeGateway>();
}

builder.Services.AddControllers();
builder.Services.AddOpenApi();

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
{
    options.MapInboundClaims = false;
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidIssuer = jwt.Issuer,
        ValidAudience = jwt.Audience,
        IssuerSigningKey = jwt.SigningKey(),
        NameClaimType = JwtRegisteredClaimNames.Sub,
        RoleClaimType = "role",
        ClockSkew = TimeSpan.Zero,
    };
    options.Events = new JwtBearerEvents
    {
        // A blocked user or a changed password ends the session within 1 minute (NFR-33).
        OnTokenValidated = async context =>
        {
            var principal = context.Principal!;
            var valid = Guid.TryParse(principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out var userId)
                && await context.HttpContext.RequestServices.GetRequiredService<IAuthService>().IsSessionValidAsync(
                    userId, principal.FindFirst(JwtAccessTokenIssuer.StampClaim)?.Value ?? "", context.HttpContext.RequestAborted);
            if (!valid)
            {
                context.Fail("Session ended.");
            }
        },
    };
});

// Default deny: an endpoint is public only when it says [AllowAnonymous] (NFR-23).
builder.Services.AddAuthorizationBuilder()
    .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, ct) =>
    {
        context.HttpContext.Response.Headers.RetryAfter = "60";
        await context.HttpContext.Response.WriteAsJsonAsync(new ApiError(Messages.TooManyRequests), ct);
    };
    options.AddPolicy("login", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1) }));
});

var app = builder.Build();

app.UseMiddleware<ErrorHandlingMiddleware>();
app.UseSerilogRequestLogging();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi().AllowAnonymous();
}

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseRateLimiter();
app.UseMiddleware<MustChangePasswordMiddleware>();
app.UseAuthorization();
app.MapControllers();

using (var scope = app.Services.CreateScope())
{
    var adminEmail = app.Configuration["Seed:AdminEmail"] ?? "admin@emarket.local";
    var password = await scope.ServiceProvider.GetRequiredService<AdminSeeder>().SeedAsync(adminEmail);
    if (password is not null)
    {
        // SRS 6.7: shown once in the console and not stored in any file or log sink.
        Console.WriteLine($"First admin created: {adminEmail} / {password} (change it at first login)");
    }
}

app.Run();

public partial class Program;
