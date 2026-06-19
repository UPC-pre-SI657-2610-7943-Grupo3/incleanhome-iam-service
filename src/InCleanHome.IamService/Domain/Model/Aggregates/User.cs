using System.Text.Json.Serialization;
using InCleanHome.IamService.Domain.Model.ValueObjects;

namespace InCleanHome.IamService.Domain.Model.Aggregates;

/// <summary>
///     User aggregate root for the IAM bounded context.
/// </summary>
public class User
{
    public int Id { get; private set; }
    public string Email { get; private set; } = string.Empty;

    [JsonIgnore] public string PasswordHash { get; private set; } = string.Empty;

    public string Role { get; private set; } = UserRole.Client;
    public bool IsVerified { get; private set; }
    public bool DocumentsVerified { get; private set; }

    /// <summary>True once the worker has uploaded both required documents. Stays unverified until admin approves.</summary>
    public bool DocumentsUploaded { get; private set; }

    /// <summary>True when admin explicitly rejected the worker's documents and worker has not yet re-uploaded.</summary>
    public bool DocumentsRejected { get; private set; }

    [JsonIgnore] public string? ResetToken { get; private set; }
    [JsonIgnore] public DateTimeOffset? ResetTokenExpiresAt { get; private set; }

    public DateTimeOffset? SuspendedUntil { get; private set; }
    public string? SuspensionReason { get; private set; }

    [JsonIgnore] public string? DeviceToken { get; private set; }

    public User() { }

    public User(string email, string passwordHash, string role)
    {
        Email             = email;
        PasswordHash      = passwordHash;
        Role              = UserRole.IsValid(role) ? role : UserRole.Client;
        IsVerified        = role == UserRole.Client;
        DocumentsVerified = role == UserRole.Client;
    }

    public User UpdatePasswordHash(string passwordHash) { PasswordHash = passwordHash; return this; }
    public User UpdateEmail(string email)               { Email = email; return this; }
    public User Verify()                                { IsVerified = true; return this; }

    public User MarkDocumentsAsUploaded()
    {
        DocumentsUploaded = true;
        DocumentsRejected = false;
        return this;
    }

    public User MarkDocumentsAsVerified()
    {
        DocumentsVerified = true;
        DocumentsUploaded = true;
        DocumentsRejected = false;
        IsVerified        = true;
        return this;
    }

    public User MarkDocumentsAsRejected()
    {
        DocumentsVerified = false;
        DocumentsUploaded = false;
        DocumentsRejected = true;
        IsVerified        = false;
        return this;
    }

    public User SetResetToken(string token, DateTimeOffset expiresAt)
    {
        ResetToken = token;
        ResetTokenExpiresAt = expiresAt;
        return this;
    }

    public bool IsResetTokenValid(string token)
        => !string.IsNullOrEmpty(ResetToken)
           && ResetToken == token
           && ResetTokenExpiresAt.HasValue
           && ResetTokenExpiresAt.Value > DateTimeOffset.UtcNow;

    public User ClearResetToken()
    {
        ResetToken = null;
        ResetTokenExpiresAt = null;
        return this;
    }

    public User Suspend(TimeSpan duration, string reason)
    {
        var now = DateTimeOffset.UtcNow;
        var baseTime = SuspendedUntil.HasValue && SuspendedUntil.Value > now ? SuspendedUntil.Value : now;
        SuspendedUntil   = baseTime.Add(duration);
        SuspensionReason = reason;
        return this;
    }

    public User ClearSuspension()
    {
        SuspendedUntil   = null;
        SuspensionReason = null;
        return this;
    }

    public User UpdateDeviceToken(string? token)
    {
        DeviceToken = string.IsNullOrWhiteSpace(token) ? null : token;
        return this;
    }

    public bool IsCurrentlySuspended()
        => SuspendedUntil.HasValue && SuspendedUntil.Value > DateTimeOffset.UtcNow;
}
