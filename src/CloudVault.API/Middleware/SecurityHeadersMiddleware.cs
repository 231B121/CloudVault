namespace CloudVault.API.Middleware;

public class SecurityHeadersMiddleware
{
    private readonly RequestDelegate _next;

    public SecurityHeadersMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        context.Response.Headers.Append("X-Content-Type-Options", "nosniff");
        context.Response.Headers.Append("X-Frame-Options", "DENY");
        context.Response.Headers.Append("X-XSS-Protection", "1; mode=block");
        context.Response.Headers.Append("Referrer-Policy", "strict-origin-when-cross-origin");
        context.Response.Headers.Append("Content-Security-Policy", "default-src 'self' 'unsafe-inline' 'unsafe-eval' https: data: blob:;");

        // Prevent stale browser caching on dynamic API requests so file list updates immediately
        if (context.Request.Path.StartsWithSegments("/api"))
        {
            context.Response.Headers.Append("Cache-Control", "no-cache, no-store, must-revalidate, max-age=0");
            context.Response.Headers.Append("Pragma", "no-cache");
            context.Response.Headers.Append("Expires", "0");
        }

        await _next(context);
    }
}
