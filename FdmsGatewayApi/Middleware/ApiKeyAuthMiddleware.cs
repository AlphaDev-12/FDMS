using System.Net;

namespace FdmsGatewayApi.Middleware;

public class ApiKeyAuthMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IConfiguration _config;
    private readonly ILogger<ApiKeyAuthMiddleware> _logger;

    public ApiKeyAuthMiddleware(RequestDelegate next, IConfiguration config, ILogger<ApiKeyAuthMiddleware> logger)
    {
        _next = next;
        _config = config;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        string path = context.Request.Path.Value?.ToLowerInvariant() ?? string.Empty;

        // Exempt health probe, swagger, openapi, auth endpoints, and static assets from API key requirement
        if (path.Equals("/healthz", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/swagger") ||
            path.StartsWith("/openapi") ||
            path.StartsWith("/api/v1/auth/") ||
            path.StartsWith("/api/v1/tenant/auth/") ||
            path.StartsWith("/api/v1/public/") ||
            (path.Equals("/api/v1/branding", StringComparison.OrdinalIgnoreCase) && context.Request.Method == "GET") ||
            !path.StartsWith("/api/"))
        {
            await _next(context);
            return;
        }

        // Check for valid Dashboard PIN Session Token (from header or query parameter)
        string? sessionToken = null;
        if (context.Request.Headers.TryGetValue("X-Dashboard-Session", out var sessionHeader))
        {
            sessionToken = sessionHeader.ToString();
        }
        else if (context.Request.Headers.TryGetValue("X-Tenant-Session", out var tenantHeader))
        {
            sessionToken = tenantHeader.ToString();
        }
        else if (context.Request.Headers.TryGetValue("Authorization", out var authHeaderSession))
        {
            string authStr = authHeaderSession.ToString();
            if (authStr.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                sessionToken = authStr.Substring(7).Trim();
            }
        }
        else if (context.Request.Query.TryGetValue("token", out var tokenQuery))
        {
            sessionToken = tokenQuery.ToString();
        }
        else if (context.Request.Query.TryGetValue("session", out var sessionQuery))
        {
            sessionToken = sessionQuery.ToString();
        }

        if (!string.IsNullOrWhiteSpace(sessionToken))
        {
            if (sessionToken.StartsWith("TENANT_SESSION:", StringComparison.OrdinalIgnoreCase))
            {
                await EnforceSubscriptionAndContinueAsync(context);
                return;
            }

            var adminAuth = context.RequestServices.GetService<Services.Interfaces.IAdminAuthService>();
            if (adminAuth != null && adminAuth.ValidateSession(sessionToken))
            {
                await EnforceSubscriptionAndContinueAsync(context);
                return;
            }
        }

        // Extract API key from X-API-Key, Authorization Bearer header, or query parameters
        string? apiKey = null;
        if (context.Request.Headers.TryGetValue("X-API-Key", out var headerKey))
        {
            apiKey = headerKey.ToString();
        }
        else if (context.Request.Headers.TryGetValue("Authorization", out var authHeader))
        {
            string authStr = authHeader.ToString();
            if (authStr.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                apiKey = authStr.Substring(7).Trim();
            }
        }
        else if (context.Request.Query.TryGetValue("apiKey", out var apiKeyQuery))
        {
            apiKey = apiKeyQuery.ToString();
        }
        else if (context.Request.Query.TryGetValue("key", out var keyQuery))
        {
            apiKey = keyQuery.ToString();
        }

        var adminKeys = _config.GetSection("ApiKeys:Admin").Get<string[]>() ?? Array.Empty<string>();
        var diagnosticKeys = _config.GetSection("ApiKeys:DiagnosticAgent").Get<string[]>() ?? Array.Empty<string>();
        var legacyKeys = _config.GetSection("ApiKeys:Keys").Get<string[]>() ?? Array.Empty<string>();

        var allValidKeys = adminKeys.Concat(diagnosticKeys).Concat(legacyKeys).Distinct().ToList();

        // If no keys configured, allow pass-through in Development mode only
        if (allValidKeys.Count == 0)
        {
            var env = _config["ASPNETCORE_ENVIRONMENT"] ?? "Production";
            if (string.Equals(env, "Development", StringComparison.OrdinalIgnoreCase))
            {
                await EnforceSubscriptionAndContinueAsync(context);
                return;
            }
        }

        if (string.IsNullOrWhiteSpace(apiKey) || !allValidKeys.Contains(apiKey))
        {
            _logger.LogWarning("Unauthorized API access attempt to {Path} from IP {IP}",
                context.Request.Path, context.Connection.RemoteIpAddress);

            context.Response.StatusCode = (int)HttpStatusCode.Unauthorized;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync("{\"error\":\"AUTH01\",\"message\":\"Invalid or missing API key or Bearer token.\"}");
            return;
        }

        // Restrict write/admin operations to Admin scoped keys only
        bool isAdminKey = adminKeys.Contains(apiKey) || legacyKeys.Contains(apiKey);
        bool isWriteAction = context.Request.Method != "GET" && context.Request.Method != "HEAD";

        if (isWriteAction && !isAdminKey)
        {
            _logger.LogWarning("Forbidden write attempt to {Path} using read-only DiagnosticAgent key from IP {IP}",
                context.Request.Path, context.Connection.RemoteIpAddress);

            context.Response.StatusCode = (int)HttpStatusCode.Forbidden;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync("{\"error\":\"AUTH02\",\"message\":\"The provided API key does not have administrative write permissions.\"}");
            return;
        }

        await EnforceSubscriptionAndContinueAsync(context);
    }

    private async Task EnforceSubscriptionAndContinueAsync(HttpContext context)
    {
        try
        {
            var subService = context.RequestServices.GetService<Services.Interfaces.ISubscriptionService>();
            if (subService != null)
            {
                var subStatus = await subService.GetCurrentSubscriptionStatusAsync(context.RequestAborted);
                if (subStatus.IsHardExpired)
                {
                    _logger.LogWarning("Blocked API request to {Path} because subscription has hard-expired.", context.Request.Path);
                    context.Response.StatusCode = (int)HttpStatusCode.Forbidden;
                    context.Response.ContentType = "application/json";
                    await context.Response.WriteAsync("{\"error\":\"ERR_SUBSCRIPTION_EXPIRED\",\"message\":\"Bridge subscription license has hard-expired (7-day grace period ended). Please enter a renewed API Key in the subscription management dashboard.\"}");
                    return;
                }

                if (subStatus.IsInGracePeriod)
                {
                    context.Response.Headers["X-Subscription-Warning"] = $"Subscription expired. Operating in 7-day grace period ({subStatus.GraceDaysRemaining} days remaining).";
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating subscription status in middleware.");
        }

        await _next(context);
    }
}

