using EMarket.BLL.Common;
using EMarket.Shared.Constants;
using EMarket.Shared.Results;
using Microsoft.EntityFrameworkCore;
using Serilog.Context;

namespace EMarket.Api.Infrastructure;

/// <summary>Adds X-Correlation-Id to every request (NFR-6) and never leaks a stack trace (NFR-45).</summary>
public sealed class ErrorHandlingMiddleware(RequestDelegate next, ILogger<ErrorHandlingMiddleware> logger)
{
    private const string Header = "X-Correlation-Id";

    public async Task InvokeAsync(HttpContext context)
    {
        var supplied = context.Request.Headers[Header].ToString();
        var correlationId = IsSafe(supplied) ? supplied : Guid.NewGuid().ToString("N");
        context.Response.Headers[Header] = correlationId;

        using (LogContext.PushProperty("CorrelationId", correlationId))
        {
            try
            {
                await next(context);
            }
            catch (AppException ex)
            {
                await WriteAsync(context, ex.StatusCode, ex.Message, correlationId);
            }
            catch (DbUpdateConcurrencyException)
            {
                await WriteAsync(context, StatusCodes.Status409Conflict, Messages.Conflict, correlationId);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Unhandled error");
                await WriteAsync(context, StatusCodes.Status500InternalServerError, Messages.ServiceDown, correlationId);
            }
        }
    }

    // A client-supplied id goes into logs, so only short plain values are accepted.
    private static bool IsSafe(string value) =>
        value.Length is > 0 and <= 40 && value.All(c => char.IsAsciiLetterOrDigit(c) || c == '-');

    private static Task WriteAsync(HttpContext context, int status, string message, string correlationId)
    {
        context.Response.StatusCode = status;
        return context.Response.WriteAsJsonAsync(new ApiError(message, correlationId));
    }
}
