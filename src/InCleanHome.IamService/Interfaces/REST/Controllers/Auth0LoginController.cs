using System.Net.Mime;
using InCleanHome.IamService.Application.Internal.OutboundServices;
using InCleanHome.IamService.Domain.Model.Aggregates;
using InCleanHome.IamService.Domain.Model.ValueObjects;
using InCleanHome.IamService.Domain.Repositories;
using InCleanHome.IamService.Domain.Services.External;
using InCleanHome.IamService.Infrastructure.ExternalServices.ProfileService;
using InCleanHome.IamService.Infrastructure.Messaging.Events;
using InCleanHome.IamService.Infrastructure.Pipeline;
using InCleanHome.IamService.Interfaces.REST.Transform;
using MassTransit;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace InCleanHome.IamService.Interfaces.REST.Controllers;

/// <summary>
/// Auth0 authentication endpoints.
///
///   GET  /api/auth/auth0/status                  — check if Auth0 is enabled
///   POST /api/auth/auth0/login                   — exchange Auth0 token for our JWT
///   POST /api/auth/auth0/complete-registration   — finish registration with role + profile data
///
/// FLOW:
///   1) Frontend → Auth0 Universal Login → callback with access_token.
///   2) Frontend → POST /login { accessToken }.
///   3) If user EXISTS in DB → return our JWT + data → frontend redirects by role.
///   4) If NOT → return { needsRoleSelection: true, email, name } WITHOUT creating
///      anything. Frontend goes to /welcome.
///   5) In /welcome user picks role + fills profile data.
///   6) Frontend → POST /complete-registration { accessToken, role, name, phone, ... }
///      → backend creates User AND calls Profile Service over HTTP to create the
///      profile. Returns our JWT.
/// </summary>
[ApiController]
[Route("api/auth/auth0")]
[Produces(MediaTypeNames.Application.Json)]
[SwaggerTag("Auth0 — login with external identity provider")]
public class Auth0LoginController(
    IIdentityProvider identityProvider,
    IUserRepository userRepository,
    ITokenService tokenService,
    IProfileServiceClient profileServiceClient,
    IUnitOfWork unitOfWork,
    IPublishEndpoint publishEndpoint,
    IConfiguration configuration,
    ILogger<Auth0LoginController> logger) : ControllerBase
{
    public record Auth0LoginRequest(string AccessToken);

    public record Auth0CompleteRegistrationRequest(
        string AccessToken,
        string Role,
        // Common
        string? Name,
        string? Phone,
        // Worker only
        int? Age,
        string? Gender,
        List<string>? ServiceTypes,
        List<string>? Zones,
        decimal? HourlyRate,
        decimal? HourlyRateSunday,
        int? ExperienceYears,
        string? Bio);

    [HttpGet("status")]
    [AllowAnonymous]
    [SwaggerOperation("Auth0 Status", "Returns whether Auth0 is enabled on this backend.")]
    public IActionResult Status() => Ok(new { enabled = identityProvider.IsEnabled });

    [HttpPost("login")]
    [AllowAnonymous]
    [SwaggerOperation("Auth0 Login",
        "Receives the Auth0 access_token, validates the signature and queries /userinfo. " +
        "If the user exists in DB returns our JWT. If not, returns needsRoleSelection=true " +
        "so the frontend goes to /welcome.")]
    public async Task<IActionResult> Login([FromBody] Auth0LoginRequest body)
    {
        if (!identityProvider.IsEnabled)
            return StatusCode(503, new { error = "Auth0 is not enabled on this backend" });

        if (string.IsNullOrWhiteSpace(body?.AccessToken))
            return BadRequest(new { error = "accessToken is required" });

        var info = await identityProvider.ValidateAndGetUserInfoAsync(body.AccessToken);
        if (info is null)
            return Unauthorized(new { error = "Invalid Auth0 token" });

        var user = await userRepository.FindByEmailAsync(info.Email);

        // Admin auto-provisioning (same behaviour as monolith).
        if (user is null && IsAdminEmail(info.Email))
        {
            user = new User(info.Email, "AUTH0_" + Guid.NewGuid().ToString("N"), UserRole.Admin);
            user.Verify();
            await userRepository.AddAsync(user);
            await unitOfWork.CompleteAsync();
            logger.LogInformation("[Auth0] Admin auto-provisioned: {Email}", info.Email);
        }

        if (user is null)
        {
            // New non-admin user: frontend will go to /welcome.
            return Ok(new
            {
                needsRoleSelection = true,
                email   = info.Email,
                name    = info.Name,
                picture = info.PictureUrl
            });
        }

        return Ok(await BuildLoginResponse(user, info.Name));
    }

    [HttpPost("complete-registration")]
    [AllowAnonymous]
    [SwaggerOperation("Auth0 — Complete registration",
        "User picked their role in /welcome and filled the data. We validate the " +
        "Auth0 token again (security), create the User in DB, then call Profile Service " +
        "over HTTP to create the full Profile. Workers stay pending document upload.")]
    public async Task<IActionResult> CompleteRegistration([FromBody] Auth0CompleteRegistrationRequest body)
    {
        if (!identityProvider.IsEnabled)
            return StatusCode(503, new { error = "Auth0 is not enabled on this backend" });

        if (string.IsNullOrWhiteSpace(body?.AccessToken))
            return BadRequest(new { error = "accessToken is required" });

        // Only client/worker valid here.
        var roleLower = (body.Role ?? string.Empty).Trim().ToLowerInvariant();
        if (roleLower != "client" && roleLower != "worker")
            return BadRequest(new { error = "role must be 'client' or 'worker'" });

        if (string.IsNullOrWhiteSpace(body.Name))
            return BadRequest(new { error = "name is required" });
        if (string.IsNullOrWhiteSpace(body.Phone))
            return BadRequest(new { error = "phone is required" });

        if (roleLower == "worker")
        {
            if (body.Age is null || body.Age < 18 || body.Age > 70)
                return BadRequest(new { error = "age must be between 18 and 70" });
            if (string.IsNullOrWhiteSpace(body.Gender))
                return BadRequest(new { error = "gender is required" });
            if (body.ServiceTypes is null || body.ServiceTypes.Count == 0)
                return BadRequest(new { error = "Select at least one service type" });
            if (body.HourlyRate is null || body.HourlyRate < 10)
                return BadRequest(new { error = "hourlyRate must be >= 10" });
        }

        var info = await identityProvider.ValidateAndGetUserInfoAsync(body.AccessToken);
        if (info is null)
            return Unauthorized(new { error = "Invalid Auth0 token" });

        // If user already exists, return their current session (don't overwrite profile).
        var existing = await userRepository.FindByEmailAsync(info.Email);
        if (existing is not null)
            return Ok(await BuildLoginResponse(existing, info.Name));

        if (IsAdminEmail(info.Email))
            return StatusCode(403, new { error = "Reserved email" });

        // Create User aggregate.
        var randomPwd = "AUTH0_" + Guid.NewGuid().ToString("N");
        var role = roleLower == "worker" ? UserRole.Worker : UserRole.Client;
        var user = new User(info.Email, randomPwd, role);
        user.Verify();
        await userRepository.AddAsync(user);
        await unitOfWork.CompleteAsync();

        // Generate a temporary JWT so we can authorize the call to Profile Service.
        var token = tokenService.GenerateToken(user);

        // Create the Profile via HTTP. If this fails, rollback the user.
        bool profileCreated;
        if (role == UserRole.Client)
        {
            profileCreated = await profileServiceClient.CreateClientProfileAsync(
                user.Id, body.Name!, body.Phone!, token);
        }
        else
        {
            profileCreated = await profileServiceClient.CreateWorkerProfileAsync(
                user.Id, body.Name!, body.Phone!,
                body.Age!.Value, body.Gender!,
                body.ServiceTypes!, body.Zones ?? new List<string>(),
                body.HourlyRate!.Value, body.ExperienceYears ?? 0,
                body.Bio ?? string.Empty, token);
        }

        if (!profileCreated)
        {
            // Best-effort rollback.
            userRepository.Remove(user);
            await unitOfWork.CompleteAsync();
            logger.LogError(
                "Profile creation failed for user {UserId}. User rolled back. Frontend should retry.",
                user.Id);
            return StatusCode(502, new { error = "Profile service unreachable. Try again in a moment." });
        }

        // Publish UserRegistered event (Communication Service will send welcome notification).
        try
        {
            await publishEndpoint.Publish(new UserRegisteredEvent
            {
                UserId = user.Id,
                Email  = user.Email,
                Role   = user.Role
            });
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to publish UserRegistered. Continuing.");
        }

        return Ok(await BuildLoginResponse(user, body.Name!, createdNewUser: true));
    }

    // ─────────────────────────────────────────────────────────────────
    private bool IsAdminEmail(string email)
    {
        var adminEmail = Environment.GetEnvironmentVariable("ADMIN_EMAIL")
                         ?? configuration["AdminSeed:Email"]
                         ?? "admin@incleanhome.pe";
        return string.Equals(email, adminEmail, StringComparison.OrdinalIgnoreCase);
    }

    private async Task<object> BuildLoginResponse(User user, string fallbackName, bool createdNewUser = false)
    {
        var token = tokenService.GenerateToken(user);

        // Resolve name/phone from Profile Service so the frontend gets the same
        // shape as the monolith. Uses our own freshly issued JWT.
        string name  = fallbackName;
        string? phone = null;

        ProfileSummary? p = user.Role switch
        {
            UserRole.Worker => await profileServiceClient.GetWorkerProfileAsync(user.Id, token),
            UserRole.Client => await profileServiceClient.GetClientProfileAsync(user.Id, token),
            _ => null
        };
        if (p is not null) { name = p.Name; phone = p.Phone; }

        var payload = UserPayloadFromEntityAssembler.FromUserAndProfile(user, name, phone);
        return new
        {
            user           = payload,
            token,
            createdNewUser,
            authProvider   = "auth0"
        };
    }
}
