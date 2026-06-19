namespace InCleanHome.IamService.Infrastructure.Messaging.Events;

/// <summary>
/// Integration events published by IAM Service to the RabbitMQ broker.
/// Other microservices (mainly Communication) consume these to react.
///
/// IMPORTANT — contracts:
///   These records define the wire contract. If you change a property name
///   or type, every consumer of the event must change too. Treat them as
///   versioned contracts (add new properties; don't remove or rename existing).
/// </summary>
public record UserRegisteredEvent
{
    public int UserId { get; init; }
    public string Email { get; init; } = string.Empty;
    public string Role { get; init; } = string.Empty;
    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;
}

public record WorkerDocumentsApprovedEvent
{
    public int UserId { get; init; }
    public string Email { get; init; } = string.Empty;
    public string? ApprovedBy { get; init; }
    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;
}

public record WorkerDocumentsRejectedEvent
{
    public int UserId { get; init; }
    public string Email { get; init; } = string.Empty;
    public string? Reason { get; init; }
    public string? RejectedBy { get; init; }
    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;
}

public record UserSuspendedEvent
{
    public int UserId { get; init; }
    public string Email { get; init; } = string.Empty;
    public string Reason { get; init; } = string.Empty;
    public DateTimeOffset SuspendedUntil { get; init; }
    public string? SuspendedBy { get; init; }
    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;
}

public record UserSuspensionClearedEvent
{
    public int UserId { get; init; }
    public string Email { get; init; } = string.Empty;
    public string? ClearedBy { get; init; }
    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;
}

public record UserDeletedEvent
{
    public int UserId { get; init; }
    public string Email { get; init; } = string.Empty;
    public string? DeletedBy { get; init; }
    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Published whenever a user registers, updates or clears their FCM device token
/// (via POST /api/auth/device-token). Communication Service maintains a local
/// projection of (userId, token) so it can send pushes without HTTP-calling IAM.
/// </summary>
public record UserDeviceTokenUpdatedEvent
{
    public int UserId { get; init; }
    public string? Token { get; init; }
    public string Role { get; init; } = string.Empty;
    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;
}
