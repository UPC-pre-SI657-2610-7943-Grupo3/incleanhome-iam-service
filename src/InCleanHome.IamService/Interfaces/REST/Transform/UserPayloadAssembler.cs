using InCleanHome.IamService.Domain.Model.Aggregates;

namespace InCleanHome.IamService.Interfaces.REST.Transform;

/// <summary>
/// Assembles the public user payload returned to the frontend.
/// In the monolith this lived in the Profiles module because it merged
/// User + Profile data. Here we only return the IAM portion; the frontend
/// can call Profile Service separately for name/phone/avatar.
/// </summary>
public static class UserPayloadAssembler
{
    public static object FromUser(User user)
        => new
        {
            id                = user.Id,
            email             = user.Email,
            role              = user.Role,
            isVerified        = user.IsVerified,
            documentsVerified = user.DocumentsVerified,
            documentsUploaded = user.DocumentsUploaded,
            documentsRejected = user.DocumentsRejected,
            suspendedUntil    = user.SuspendedUntil,
            suspensionReason  = user.SuspensionReason,
            isSuspended       = user.IsCurrentlySuspended()
        };
}
