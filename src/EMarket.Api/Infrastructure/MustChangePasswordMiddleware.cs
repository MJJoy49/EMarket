using EMarket.Shared.Constants;
using EMarket.Shared.Results;

namespace EMarket.Api.Infrastructure;

/// <summary>FR-1.17: until a seeded or new admin changes the password, only the auth endpoints answer.</summary>
public sealed class MustChangePasswordMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (context.User.FindFirst("mustChangePassword")?.Value == "true"
            && !context.Request.Path.StartsWithSegments("/api/v1/auth"))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new ApiError("Please change your password first."));
            return;
        }

        await next(context);
    }
}
