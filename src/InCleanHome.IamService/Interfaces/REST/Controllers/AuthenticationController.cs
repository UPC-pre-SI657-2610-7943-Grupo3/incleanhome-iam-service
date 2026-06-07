using System.Net.Mime;
using InCleanHome.IamService.Application.Internal.OutboundServices;
using InCleanHome.IamService.Domain.Model.Aggregates;
using InCleanHome.IamService.Domain.Model.Commands;
using InCleanHome.IamService.Domain.Model.ValueObjects;
using InCleanHome.IamService.Domain.Services;
using InCleanHome.IamService.Infrastructure.Pipeline;
using InCleanHome.IamService.Interfaces.REST.Transform;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace InCleanHome.IamService.Interfaces.REST.Controllers;

/// <summary>
/// Authentication endpoints consumed by the Vue frontend.
/// </summary>
/// <remarks>
/// Endpoints:
/// <list type="bullet">
///   <item><description>POST /api/v1/auth/register    — local password-based registration</description></item>
///   <item><description>POST /api/v1/auth/login       — local password-based login</description></item>
///   <item><description>GET  /api/v1/auth/me          — return current user (auth required)</description></item>
///   <item><description>POST /api/v1/auth/worker/upload-document  — worker uploads PDF</description></item>
///   <item><description>POST /api/v1/auth/device-token            — register FCM token</description></item>
/// </list>
/// In the monolith the /me endpoint also resolved name/phone via the Profiles module.
/// In the microservice split, the IAM service returns ONLY the IAM portion of the user.
/// The frontend must call Profile Service separately for profile data.
/// </remarks>
[ApiController]
[Route("api/v1/auth")]
[Produces(MediaTypeNames.Application.Json)]
[SwaggerTag("Authentication & worker onboarding")]
public class AuthenticationController(
    IUserCommandService userCommandService,
    ITokenService tokenService) : ControllerBase
{
    public record RegisterRequest(string Email, string Password, string Role);

    [HttpGet("me")]
    [SwaggerOperation("Get Current User",
        "Returns the current user's IAM data including suspension status. " +
        "Profile data (name, phone, avatar) is NOT included; call Profile Service for that.")]
    public IActionResult Me()
    {
        var current = (User?)HttpContext.Items["User"];
        if (current is null) return Unauthorized();
        return Ok(UserPayloadAssembler.FromUser(current));
    }

    public record UploadDocumentResource(string DocumentType, string FileBase64, string FileName);

    [HttpPost("worker/upload-document")]
    [SwaggerOperation("Upload Worker Document",
        "Upload a PDF (background_check or experience). If documents were previously rejected, " +
        "this clears the rejected flag once both documents are present.")]
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
        "Stores the Firebase Cloud Messaging token for the user's current device.")]
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
}
