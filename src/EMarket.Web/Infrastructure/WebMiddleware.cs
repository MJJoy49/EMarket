using EMarket.Web.Clients;

namespace EMarket.Web;

/// <summary>Adds a cache header to a file result, for public images.</summary>
public sealed class CachedResult(IResult inner) : IResult
{
    public Task ExecuteAsync(HttpContext httpContext)
    {
        httpContext.Response.Headers.CacheControl = "public, max-age=86400";
        return inner.ExecuteAsync(httpContext);
    }
}

/// <summary>NFR-35: the security headers. There are no inline scripts or styles, so none are allowed.</summary>
public sealed class WebSecurityHeaders(RequestDelegate next, IConfiguration config)
{
    private readonly string formAction = "'self' " + (config["Csp:FormActions"] ?? config["Api:BaseUrl"] ?? "https://localhost:7001");

    public Task InvokeAsync(HttpContext context)
    {
        var headers = context.Response.Headers;
        headers["X-Content-Type-Options"] = "nosniff";
        headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
        headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
        // form-action lists the gateway, because the checkout form is answered with a redirect to the payment page.
        headers["Content-Security-Policy"] =
            $"default-src 'self'; img-src 'self' data:; style-src 'self'; script-src 'self'; frame-ancestors 'none'; base-uri 'self'; form-action {formAction}";
        return next(context);
    }
}

/// <summary>FR-21.9: when the API says it is under maintenance, visitors see the Maintenance page.</summary>
public sealed class MaintenanceRedirect(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (MaintenanceException) when (!context.Response.HasStarted && !context.Request.Path.StartsWithSegments("/Maintenance"))
        {
            context.Response.Redirect("/Maintenance");
        }
    }
}

/// <summary>FR-1.17: until the first-login password is changed, every page sends the user to Change Password.</summary>
public sealed class MustChangePasswordRedirect(RequestDelegate next)
{
    public Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path;
        if (context.User.FindFirst("mustChangePassword")?.Value == "true"
            && !path.StartsWithSegments("/change-password") && !path.StartsWithSegments("/logout") && !path.StartsWithSegments("/css")
            && !path.StartsWithSegments("/lib") && !path.StartsWithSegments("/js") && !path.StartsWithSegments("/Error"))
        {
            context.Response.Redirect("/change-password");
            return Task.CompletedTask;
        }

        return next(context);
    }
}
