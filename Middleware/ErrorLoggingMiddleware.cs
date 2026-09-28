using FalazaLodge.Api.Data;

namespace FalazaLodge.Api.Middleware;

public sealed class ErrorLoggingMiddleware(RequestDelegate next, ILogger<ErrorLoggingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context, IServiceScopeFactory scopeFactory)
    {
        try
        {
            await next(context);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unhandled error for {Method} {Path}. TraceId: {TraceId}", context.Request.Method, context.Request.Path, context.TraceIdentifier);

            try
            {
                using var scope = scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                db.ErrorLogs.Add(new ErrorLog
                {
                    TraceId = context.TraceIdentifier,
                    Method = context.Request.Method,
                    Path = context.Request.Path,
                    ExceptionType = ex.GetType().FullName ?? ex.GetType().Name,
                    Message = ex.Message,
                    StackTrace = ex.StackTrace
                });
                await db.SaveChangesAsync();
            }
            catch (Exception persistenceError)
            {
                logger.LogError(persistenceError, "Could not persist error log for TraceId {TraceId}", context.TraceIdentifier);
            }

            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            context.Response.ContentType = "application/problem+json";
            await context.Response.WriteAsJsonAsync(new
            {
                title = "An unexpected error occurred.",
                status = 500,
                traceId = context.TraceIdentifier
            });
        }
    }
}
