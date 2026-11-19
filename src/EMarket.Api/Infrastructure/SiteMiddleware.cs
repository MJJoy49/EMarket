using EMarket.BLL.Common;
using EMarket.Shared.Constants;
using EMarket.Shared.Results;

namespace EMarket.Api.Infrastructure;

/// <summary>NFR-35: security headers on every response.</summary>
public sealed class SecurityHeadersMiddleware(RequestDelegate next)
{
    public Task InvokeAsync(HttpContext context)
    {
        var headers = context.Response.Headers;
        headers["X-Content-Type-Options"] = "nosniff";
        headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
        headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
        headers["Content-Security-Policy"] = "default-src 'none'; frame-ancestors 'none'; img-src 'self'";
        return next(context);
    }
}

/// <summary>
/// FR-21.9: with maintenance mode on, only admins (and signing in) keep working; everyone else gets 503.
/// The web app shows its Maintenance page for that answer.
/// </summary>
public sealed class MaintenanceModeMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, ISettingsReader settings)
    {
        var path = context.Request.Path;
        var exempt = path.StartsWithSegments("/api/v1/auth") || path.StartsWithSegments("/api/v1/site") || path.StartsWithSegments("/health")
            || path.StartsWithSegments("/api/v1/payments/gateway") || context.User.IsInRole(Roles.Admin);
        if (!exempt && await settings.GetIntAsync("maintenanceMode", 0, context.RequestAborted) == 1)
        {
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            context.Response.Headers.RetryAfter = "600";
            await context.Response.WriteAsJsonAsync(new ApiError("EMarket is under maintenance. Please come back soon."));
            return;
        }

        await next(context);
    }
}
