using System.Threading.RateLimiting;
using EMarket.Api.Infrastructure;
using EMarket.BLL;
using EMarket.BLL.Common;
using EMarket.BLL.Identity;
using EMarket.BLL.Messaging;
using Hangfire;
using Hangfire.SqlServer;
using EMarket.BLL.Payments;
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

builder.Services.AddBllModule(builder.Configuration.GetConnectionString("DefaultConnection")!);
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

// AD-8: Hangfire runs the timed rules. It can be switched off (Jobs:Enabled=false) for tests and one-off tools.
var jobsEnabled = !string.Equals(builder.Configuration["Jobs:Enabled"], "false", StringComparison.OrdinalIgnoreCase);
if (jobsEnabled)
{
    builder.Services.AddHangfire(config => config
        .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
        .UseSimpleAssemblyNameTypeSerializer()
        .UseRecommendedSerializerSettings()
        .UseSqlServerStorage(builder.Configuration.GetConnectionString("DefaultConnection"),
            new SqlServerStorageOptions { SchemaName = "HangFire", PrepareSchemaIfNecessary = true }));
    builder.Services.AddHangfireServer(options => options.WorkerCount = 2);
}

builder.Services.AddHealthChecks().AddCheck<DatabaseHealthCheck>("database", tags: ["ready"]);
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
app.UseMiddleware<SecurityHeadersMiddleware>();
app.UseSerilogRequestLogging();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi().AllowAnonymous();
}

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseRateLimiter();
app.UseMiddleware<MustChangePasswordMiddleware>();
app.UseMiddleware<MaintenanceModeMiddleware>();
app.UseAuthorization();
app.MapControllers();

// NFR-14: "live" answers while the process runs; "ready" fails when the database is down.
app.MapHealthChecks("/health/live", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions { Predicate = _ => false }).AllowAnonymous();
app.MapHealthChecks("/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions { Predicate = c => c.Tags.Contains("ready") }).AllowAnonymous();

if (jobsEnabled)
{
    var jobs = app.Services.GetRequiredService<IRecurringJobManager>();
    jobs.AddOrUpdate<ScheduledJobs>("expire-unpaid", x => x.ExpireUnpaidOrdersAsync(), "* * * * *");
    jobs.AddOrUpdate<OutboxProcessor>("outbox", x => x.ProcessAsync(CancellationToken.None), "* * * * *");
    jobs.AddOrUpdate<ScheduledJobs>("reconcile-payments", x => x.ReconcilePaymentsAsync(), "*/10 * * * *");
    jobs.AddOrUpdate<ScheduledJobs>("process-refunds", x => x.ProcessRefundsAsync(), "*/5 * * * *");
    jobs.AddOrUpdate<ScheduledJobs>("release-earnings", x => x.ReleaseEarningsAsync(), "0 * * * *");
    jobs.AddOrUpdate<ScheduledJobs>("cancel-unconfirmed", x => x.CancelUnconfirmedOrdersAsync(), "*/30 * * * *");
    jobs.AddOrUpdate<ScheduledJobs>("clean-up", x => x.CleanUpAsync(), "30 21 * * *"); // 03:30 in Bangladesh
    jobs.AddOrUpdate<ScheduledJobs>("ledger-check", x => x.CheckLedgerAsync(), "0 22 * * *"); // 04:00 in Bangladesh
}

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
