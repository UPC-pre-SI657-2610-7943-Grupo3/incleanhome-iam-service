using System.Net.Mime;
using InCleanHome.IamService.Domain.Model.Aggregates;
using InCleanHome.IamService.Domain.Model.Commands;
using InCleanHome.IamService.Domain.Model.Queries;
using InCleanHome.IamService.Domain.Model.ValueObjects;
using InCleanHome.IamService.Domain.Repositories;
using InCleanHome.IamService.Domain.Services;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace InCleanHome.IamService.Interfaces.REST.Controllers;

public record SuspendUserResource(int Days, string? Reason);

/// <summary>
/// Administrative endpoints for account verification / moderation.
/// Restricted to users with the <c>admin</c> role.
///
/// IMPORTANT: in the monolith these endpoints called Notifications via ACL
/// (NotificationsContextFacade). In microservices, the user notification is sent
/// by Communication Service via RabbitMQ events that UserCommandService publishes
/// (WorkerDocumentsApprovedEvent, WorkerDocumentsRejectedEvent, UserSuspendedEvent,
/// UserSuspensionClearedEvent). Result for the end user is the same — they still
/// receive the in-app + push notification.
/// </summary>
[ApiController]
[Route("api/admin")]
[Produces(MediaTypeNames.Application.Json)]
[SwaggerTag("Administration & verification")]
public class AdminController(
    IUserCommandService userCommandService,
    IUserQueryService userQueryService,
    IWorkerDocumentRepository workerDocumentRepository) : ControllerBase
{
    private bool IsAdmin(out User? current)
    {
        current = (User?)HttpContext.Items["User"];
        return current is not null && current.Role == UserRole.Admin;
    }

    [HttpGet("users")]
    [SwaggerOperation("List Users", "Returns all users (admin only).")]
    public async Task<IActionResult> ListUsers()
    {
        if (!IsAdmin(out _)) return Forbid();
        var users = await userQueryService.Handle(new GetAllUsersQuery());
        return Ok(users);
    }

    [HttpPatch("users/{id:int}/verify")]
    [SwaggerOperation("Verify User", "Activates a user account (admin only).")]
    public async Task<IActionResult> Verify(int id)
    {
        if (!IsAdmin(out _)) return Forbid();
        try
        {
            await userCommandService.Handle(new VerifyUserCommand(id));
            return Ok(new { message = "User verified" });
        }
        catch (Exception e) { return BadRequest(new { error = e.Message }); }
    }

    [HttpPatch("users/{id:int}/approve-documents")]
    [SwaggerOperation("Approve Worker Documents",
        "Approves a worker's documents and activates the account (admin only). " +
        "Publishes WorkerDocumentsApproved event for Communication Service to notify.")]
    public async Task<IActionResult> ApproveDocuments(int id)
    {
        if (!IsAdmin(out _)) return Forbid();
        try
        {
            await userCommandService.Handle(new ApproveWorkerDocumentsCommand(id));
            return Ok(new { message = "Worker documents approved" });
        }
        catch (Exception e) { return BadRequest(new { error = e.Message }); }
    }

    [HttpPatch("users/{id:int}/reject-documents")]
    [SwaggerOperation("Reject Worker Documents",
        "Rejects a worker's documents. The account stays but is marked unverified — " +
        "the worker can re-upload (admin only).")]
    public async Task<IActionResult> RejectDocuments(int id)
    {
        if (!IsAdmin(out _)) return Forbid();
        try
        {
            await userCommandService.Handle(new RejectWorkerDocumentsCommand(id));
            return Ok(new { message = "Worker documents rejected" });
        }
        catch (Exception e) { return BadRequest(new { error = e.Message }); }
    }

    [HttpPatch("users/{id:int}/suspend")]
    [SwaggerOperation("Suspend User", "Temporarily suspends a user account (admin only).")]
    public async Task<IActionResult> Suspend(int id, [FromBody] SuspendUserResource resource)
    {
        if (!IsAdmin(out _)) return Forbid();
        try
        {
            var days = resource.Days <= 0 ? 1 : resource.Days;
            await userCommandService.Handle(new SuspendUserCommand(
                id, TimeSpan.FromDays(days), resource.Reason ?? "Suspensión administrativa"));
            return Ok(new { message = "User suspended", days });
        }
        catch (Exception e) { return BadRequest(new { error = e.Message }); }
    }

    [HttpPatch("users/{id:int}/clear-suspension")]
    [SwaggerOperation("Clear Suspension", "Removes the active suspension (admin only).")]
    public async Task<IActionResult> ClearSuspension(int id)
    {
        if (!IsAdmin(out _)) return Forbid();
        try
        {
            await userCommandService.Handle(new ClearUserSuspensionCommand(id));
            return Ok(new { message = "User suspension cleared" });
        }
        catch (Exception e) { return BadRequest(new { error = e.Message }); }
    }

    [HttpGet("users/{id:int}/documents")]
    [SwaggerOperation("Get Worker Documents", "Returns documents uploaded by a worker (admin only).")]
    public async Task<IActionResult> GetDocuments(int id)
    {
        if (!IsAdmin(out _)) return Forbid();
        var docs = await workerDocumentRepository.FindByUserIdAsync(id);
        var result = docs.Select(d => new {
            d.Id, d.UserId, d.DocumentType, d.FileName, d.FileBase64, d.CreatedDate
        });
        return Ok(result);
    }

    [HttpDelete("users/{id:int}")]
    [SwaggerOperation("Delete User",
        "Permanently deletes a user account (admin only). Cannot delete admin accounts.")]
    public async Task<IActionResult> DeleteUser(int id)
    {
        if (!IsAdmin(out _)) return Forbid();
        try
        {
            await userCommandService.Handle(new DeleteUserCommand(id));
            return Ok(new { message = "User deleted successfully" });
        }
        catch (Exception e) { return BadRequest(new { error = e.Message }); }
    }
}
