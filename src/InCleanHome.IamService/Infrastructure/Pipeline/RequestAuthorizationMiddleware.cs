using InCleanHome.IamService.Application.Internal.OutboundServices;
using InCleanHome.IamService.Domain.Model.Queries;
using InCleanHome.IamService.Domain.Services;

namespace InCleanHome.IamService.Infrastructure.Pipeline;

/// <summary>
/// Attribute that marks an endpoint as accessible without a valid JWT.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public class AllowAnonymousAttribute : Attribute { }

/// <summary>
/// Resolves the authenticated user from the JWT and attaches it to
/// <c>HttpContext.Items["User"]</c>. Endpoints decorated with
/// <see cref="AllowAnonymousAttribute"/> bypass this check.
/// </summary>
public class RequestAuthorizationMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(
        HttpContext context,
        IUserQueryService userQueryService,
        ITokenService tokenService)
    {
        var path = context.Request.Path.Value ?? string.Empty;
        if (path == "/" ||
            path.StartsWith("/health", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/swagger", StringComparison.OrdinalIgnoreCase))
        {
            await next(context);
            return;
        }
        
        var endpoint = context.Request.HttpContext.GetEndpoint();
        var allowAnonymous = endpoint?.Metadata
            .Any(m => m.GetType() == typeof(AllowAnonymousAttribute)) ?? false;

        if (allowAnonymous)
        {
            await next(context);
            return;
        }

        var token = context.Request.Headers["Authorization"].FirstOrDefault()?.Split(" ").Last();
        if (string.IsNullOrEmpty(token))
        {
            context.Response.StatusCode = 401;
            await context.Response.WriteAsJsonAsync(new { error = "Missing or invalid token" });
            return;
        }

        var userId = await tokenService.ValidateToken(token);
        if (userId == null)
        {
            context.Response.StatusCode = 401;
            await context.Response.WriteAsJsonAsync(new { error = "Invalid token" });
            return;
        }

        var user = await userQueryService.Handle(new GetUserByIdQuery(userId.Value));
        if (user == null)
        {
            context.Response.StatusCode = 401;
            await context.Response.WriteAsJsonAsync(new { error = "User not found" });
            return;
        }

        context.Items["User"] = user;
        await next(context);
    }
}

public static class RequestAuthorizationMiddlewareExtensions
{
    public static IApplicationBuilder UseRequestAuthorization(this IApplicationBuilder builder)
        => builder.UseMiddleware<RequestAuthorizationMiddleware>();
}
