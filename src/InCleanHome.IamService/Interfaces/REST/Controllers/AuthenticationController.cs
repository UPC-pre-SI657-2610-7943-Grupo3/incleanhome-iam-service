using System.Net.Mime;
using InCleanHome.IamService.Application.Internal.OutboundServices;
using InCleanHome.IamService.Domain.Model.Aggregates;
using InCleanHome.IamService.Domain.Model.Commands;
using InCleanHome.IamService.Domain.Model.ValueObjects;
using InCleanHome.IamService.Domain.Services;
using InCleanHome.IamService.Infrastructure.ExternalServices.ProfileService;
using InCleanHome.IamService.Interfaces.REST.Transform;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace InCleanHome.IamService.Interfaces.REST.Controllers;

/// <summary>
/// Authentication endpoints consumed by the Vue frontend.
/// </summary>
/// <remarks>
/// Main auth runs through Auth0 (see <c>Auth0LoginController</c>). This
/// controller only exposes endpoints that the frontend uses AFTER login:
/// <list type="bullet">
///   <item><description>GET  /api/auth/me</description></item>
///   <item><description>POST /api/auth/worker/upload-document</description></item>
///   <item><description>POST /api/auth/device-token</description></item>
/// </list>
/// /login, /register/client, /register/worker, /forgot-password and /reset-password
/// were removed: Auth0 replaces them entirely (Universal Login + /welcome flow;
/// password reset is handled directly by Auth0).
/// </remarks>
[ApiController]
[Route("api/auth")]
[Produces(MediaTypeNames.Application.Json)]
[SwaggerTag("Authentication & worker onboarding")]
public class AuthenticationController(
    IUserCommandService userCommandService,
    IProfileServiceClient profileServiceClient) : ControllerBase
{
    [HttpGet("me")]
    [SwaggerOperation("Get Current User",
        "Returns the current user's data including suspension status, name and phone.")]
    public async Task<IActionResult> Me()
    {
        var current = (User?)HttpContext.Items["User"];
        if (current is null) return Unauthorized();

        // Forward the user's own JWT to Profile Service so it can authorize.
        var bearer = Request.Headers["Authorization"].FirstOrDefault()?.Replace("Bearer ", "");

        var (name, phone) = await ResolveNamePhone(current, bearer);
        var payload = UserPayloadFromEntityAssembler.FromUserAndProfile(current, name, phone);
        return Ok(payload);
    }

    public record UploadDocumentResource(string DocumentType, string FileBase64, string FileName);

    [HttpPost("worker/upload-document")]
    [SwaggerOperation("Upload Worker Document",
        "Upload a PDF (background_check or experience). If documents were previously rejected, this endpoint re-accepts them and clears the DocumentsRejected flag.")]
    public async Task<IActionResult> UploadDocument([FromBody] UploadDocumentResource resource)
    {
        var current = (User?)HttpContext.Items["User"];
        if (current is null) return Unauthorized();
        if (current.Role != UserRole.Worker) return Forbid();

        try
        {
            await userCommandService.Handle(new UploadWorkerDocumentCommand(
                current.Id, resource.DocumentType, resource.FileName, resource.FileBase64));
            return Ok(new { message = "Document uploaded successfully" });
        }
        catch (Exception e)
        {
            return BadRequest(new { error = e.Message });
        }
    }

    public record DeviceTokenResource(string? Token);

    [HttpPost("device-token")]
    [SwaggerOperation("Register FCM Device Token",
        "Stores the Firebase Cloud Messaging token of the user's current browser/device so the backend can send push notifications. Pass empty/null token to clear it (e.g. on logout).")]
    public async Task<IActionResult> RegisterDeviceToken([FromBody] DeviceTokenResource resource)
    {
        var current = (User?)HttpContext.Items["User"];
        if (current is null) return Unauthorized();

        try
        {
            await userCommandService.Handle(new RegisterDeviceTokenCommand(current.Id, resource.Token));
            return Ok(new { message = "Device token registered successfully" });
        }
        catch (Exception e)
        {
            return BadRequest(new { error = e.Message });
        }
    }

    // ─────────────────────────────────────────────────────────────────
    private async Task<(string name, string? phone)> ResolveNamePhone(User user, string? bearer)
    {
        ProfileSummary? p = user.Role == UserRole.Worker
            ? await profileServiceClient.GetWorkerProfileAsync(user.Id, bearer)
            : await profileServiceClient.GetClientProfileAsync(user.Id, bearer);

        return (p?.Name ?? user.Email, p?.Phone);
    }
}
