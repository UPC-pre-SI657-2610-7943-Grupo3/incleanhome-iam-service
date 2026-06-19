using InCleanHome.IamService.Domain.Model.Aggregates;

namespace InCleanHome.IamService.Interfaces.REST.Transform;

/// <summary>
/// Builds the JSON payload returned to the frontend for "current user" responses.
/// Includes name/phone from Profile Service so the frontend contract matches
/// the monolith (where /me and /auth0/login returned User+Profile data merged).
/// </summary>
public static class UserPayloadFromEntityAssembler
{
    public static object FromUserAndProfile(User user, string? name, string? phone)
        => new
        {
            id                = user.Id,
            email             = user.Email,
            role              = user.Role,
            name              = name ?? user.Email,
            phone             = phone,
            isVerified        = user.IsVerified,
            documentsVerified = user.DocumentsVerified,
            documentsUploaded = user.DocumentsUploaded,
            documentsRejected = user.DocumentsRejected,
            suspendedUntil    = user.SuspendedUntil,
            suspensionReason  = user.SuspensionReason,
            isSuspended       = user.IsCurrentlySuspended()
        };
}
