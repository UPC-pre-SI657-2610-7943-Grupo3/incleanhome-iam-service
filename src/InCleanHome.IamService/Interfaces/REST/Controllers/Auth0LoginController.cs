using System.Net.Mime;
using InCleanHome.IamService.Application.Internal.OutboundServices;
using InCleanHome.IamService.Domain.Model.Aggregates;
using InCleanHome.IamService.Domain.Model.ValueObjects;
using InCleanHome.IamService.Domain.Repositories;
using InCleanHome.IamService.Infrastructure.ExternalServices.Auth0;
using InCleanHome.IamService.Infrastructure.Pipeline;
using InCleanHome.IamService.Interfaces.REST.Transform;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace InCleanHome.IamService.Interfaces.REST.Controllers;

/// <summary>
/// Auth0 integration endpoints.
/// </summary>
/// <remarks>
/// Endpoints:
/// <list type="bullet">
///   <item><description>GET  /api/v1/auth/auth0/status                  — is Auth0 enabled?</description></item>
///   <item><description>POST /api/v1/auth/auth0/login                   — exchange Auth0 token for internal JWT</description></item>
///   <item><description>POST /api/v1/auth/auth0/complete-registration   — create the User (Profile must be created separately)</description></item>
/// </list>
/// IMPORTANT change from the monolith:
/// in the monolith, /complete-registration created BOTH the User and the Profile (Client or Worker)
/// in a single request because Profile lived in the same process. In this microservice split,
/// IAM only owns the User. The frontend must call Profile Service after this endpoint to create the profile.
/// </remarks>
[ApiController]
[Route("api/v1/auth/auth0")]
[Produces(MediaTypeNames.Application.Json)]
[SwaggerTag("Auth0 — login with external identity provider")]
public class Auth0LoginController(
    IAuth0Service auth0Service,
    IUserRepository userRepository,
    ITokenService tokenService,
    IUnitOfWork unitOfWork,
    IConfiguration configuration) : ControllerBase
{
    public record Auth0LoginRequest(string AccessToken);
    public record Auth0CompleteRegistrationRequest(string AccessToken, string Role);

    [HttpGet("status")]
    [AllowAnonymous]
    [SwaggerOperation("Auth0 Status", "Returns whether Auth0 is enabled on this backend.")]
    public IActionResult Status() => Ok(new { enabled = auth0Service.IsEnabled });

    [HttpPost("login")]
    [AllowAnonymous]
    [SwaggerOperation("Auth0 Login",
        "Receives the Auth0 access_token, validates its signature against the JWKS, " +
        "and queries /userinfo. If the user exists in our DB, returns our own JWT. " +
        "If not, returns needsRoleSelection=true so the frontend can show /welcome.")]
    public async Task<IActionResult> Login([FromBody] Auth0LoginRequest body)
    {
        if (!auth0Service.IsEnabled)
            return StatusCode(503, new { error = "Auth0 is not enabled on this backend" });

        if (string.IsNullOrWhiteSpace(body?.AccessToken))
            return BadRequest(new { error = "accessToken is required" });

        var info = await auth0Service.ValidateAndGetUserInfoAsync(body.AccessToken);
        if (info is null) return Unauthorized(new { error = "Invalid Auth0 token" });

        var user = await userRepository.FindByEmailAsync(info.Email);

        // Admin auto-provisioning (matches monolith behavior).
        if (user is null && IsAdminEmail(info.Email))
        {
            user = new User(info.Email, "AUTH0_" + Guid.NewGuid().ToString("N"), UserRole.Admin);
            user.Verify();
            await userRepository.AddAsync(user);
            await unitOfWork.CompleteAsync();
        }

        if (user is null)
        {
            // New non-admin user: frontend should redirect to /welcome.
            return Ok(new
            {
                needsRoleSelection = true,
                email   = info.Email,
                name    = info.Name,
                picture = info.Picture
            });
        }

        var token = tokenService.GenerateToken(user);
        return Ok(new
        {
            user         = UserPayloadAssembler.FromUser(user),
            token,
            authProvider = "auth0"
        });
    }

    [HttpPost("complete-registration")]
    [AllowAnonymous]
    [SwaggerOperation("Auth0 — Complete registration",
        "After the user picked a role in /welcome, this endpoint validates the Auth0 token " +
        "again and creates the local User. Returns our JWT plus needsProfileSetup=true so the " +
        "frontend can call Profile Service to create the worker/client profile.")]
    public async Task<IActionResult> CompleteRegistration([FromBody] Auth0CompleteRegistrationRequest body)
    {
        if (!auth0Service.IsEnabled)
            return StatusCode(503, new { error = "Auth0 is not enabled on this backend" });

        if (string.IsNullOrWhiteSpace(body?.AccessToken))
            return BadRequest(new { error = "accessToken is required" });

        var roleLower = (body.Role ?? string.Empty).Trim().ToLowerInvariant();
        if (roleLower != UserRole.Client && roleLower != UserRole.Worker)
            return BadRequest(new { error = "role must be 'client' or 'worker'" });

        var info = await auth0Service.ValidateAndGetUserInfoAsync(body.AccessToken);
        if (info is null) return Unauthorized(new { error = "Invalid Auth0 token" });

        // If the user already exists, return their existing session instead of erroring.
        var existing = await userRepository.FindByEmailAsync(info.Email);
        if (existing is not null)
        {
            var existingToken = tokenService.GenerateToken(existing);
            return Ok(new
            {
                user              = UserPayloadAssembler.FromUser(existing),
                token             = existingToken,
                needsProfileSetup = false,
                authProvider      = "auth0"
            });
        }

        if (IsAdminEmail(info.Email))
            return StatusCode(403, new { error = "Reserved email" });

        // Create the User aggregate only.
        var randomPwd = "AUTH0_" + Guid.NewGuid().ToString("N");
        var user = new User(info.Email, randomPwd, roleLower);
        user.Verify();
        await userRepository.AddAsync(user);
        await unitOfWork.CompleteAsync();

        var token = tokenService.GenerateToken(user);
        return Ok(new
        {
            user              = UserPayloadAssembler.FromUser(user),
            token,
            needsProfileSetup = true,
            authProvider      = "auth0"
        });
    }

    private bool IsAdminEmail(string email)
    {
        var adminEmail = Environment.GetEnvironmentVariable("ADMIN_EMAIL")
                         ?? configuration["AdminSeed:Email"]
                         ?? "admin@incleanhome.pe";
        return string.Equals(email, adminEmail, StringComparison.OrdinalIgnoreCase);
    }
}
