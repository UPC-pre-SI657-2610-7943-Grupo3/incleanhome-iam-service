using InCleanHome.IamService.Domain.Model.Aggregates;
using InCleanHome.IamService.Domain.Model.Commands;
using InCleanHome.IamService.Domain.Model.ValueObjects;
using InCleanHome.IamService.Domain.Repositories;
using InCleanHome.IamService.Domain.Services;
using InCleanHome.IamService.Infrastructure.Messaging.Events;
using MassTransit;

namespace InCleanHome.IamService.Application.Internal.CommandServices;

/// <summary>
/// Command handler for the IAM bounded context. Publishes integration events
/// to RabbitMQ via MassTransit after each state-changing operation so other
/// microservices (especially Communication) can react.
/// </summary>
public class UserCommandService(
    IUserRepository userRepository,
    IWorkerDocumentRepository workerDocumentRepository,
    IUnitOfWork unitOfWork,
    IPublishEndpoint publishEndpoint,
    ILogger<UserCommandService> logger) : IUserCommandService
{
    public async Task Handle(VerifyUserCommand command)
    {
        var user = await userRepository.FindByIdAsync(command.UserId)
            ?? throw new Exception($"User {command.UserId} not found");
        user.Verify();
        userRepository.Update(user);
        await unitOfWork.CompleteAsync();
    }

    public async Task Handle(ApproveWorkerDocumentsCommand command)
    {
        var user = await userRepository.FindByIdAsync(command.UserId)
            ?? throw new Exception($"User {command.UserId} not found");

        if (user.Role != UserRole.Worker)
            throw new Exception("Only worker accounts require document approval");

        user.MarkDocumentsAsVerified();
        userRepository.Update(user);
        await unitOfWork.CompleteAsync();

        await SafePublishAsync(new WorkerDocumentsApprovedEvent
        {
            UserId = user.Id,
            Email  = user.Email
        });
    }

    public async Task Handle(RejectWorkerDocumentsCommand command)
    {
        var user = await userRepository.FindByIdAsync(command.UserId)
            ?? throw new Exception($"User {command.UserId} not found");

        if (user.Role != UserRole.Worker)
            throw new Exception("Only worker accounts can have their documents rejected");

        user.MarkDocumentsAsRejected();
        userRepository.Update(user);
        await unitOfWork.CompleteAsync();

        await SafePublishAsync(new WorkerDocumentsRejectedEvent
        {
            UserId = user.Id,
            Email  = user.Email
        });
    }

    public async Task Handle(SuspendUserCommand command)
    {
        var user = await userRepository.FindByIdAsync(command.UserId)
            ?? throw new Exception($"User {command.UserId} not found");
        user.Suspend(command.Duration, command.Reason);
        userRepository.Update(user);
        await unitOfWork.CompleteAsync();

        await SafePublishAsync(new UserSuspendedEvent
        {
            UserId         = user.Id,
            Email          = user.Email,
            Reason         = command.Reason,
            SuspendedUntil = user.SuspendedUntil ?? DateTimeOffset.UtcNow
        });
    }

    public async Task Handle(ClearUserSuspensionCommand command)
    {
        var user = await userRepository.FindByIdAsync(command.UserId)
            ?? throw new Exception($"User {command.UserId} not found");
        user.ClearSuspension();
        userRepository.Update(user);
        await unitOfWork.CompleteAsync();

        await SafePublishAsync(new UserSuspensionClearedEvent
        {
            UserId = user.Id,
            Email  = user.Email
        });
    }

    public async Task Handle(UploadWorkerDocumentCommand command)
    {
        if (!DocumentType.IsValid(command.DocumentType))
            throw new Exception("Invalid document type");

        var user = await userRepository.FindByIdAsync(command.UserId)
            ?? throw new Exception("User not found");

        if (user.Role != UserRole.Worker)
            throw new Exception("Only workers can upload documents");

        var existing = (await workerDocumentRepository.FindByUserIdAsync(command.UserId)).ToList();
        foreach (var old in existing.Where(d => d.DocumentType == command.DocumentType))
            workerDocumentRepository.Remove(old);

        var doc = new WorkerDocument(command.UserId, command.DocumentType, command.FileName, command.FileBase64);
        await workerDocumentRepository.AddAsync(doc);

        var finalTypes = existing
            .Where(d => d.DocumentType != command.DocumentType)
            .Select(d => d.DocumentType)
            .Append(command.DocumentType)
            .ToHashSet();

        if (finalTypes.Contains(DocumentType.BackgroundCheck) && finalTypes.Contains(DocumentType.Experience))
        {
            user.MarkDocumentsAsUploaded();
            userRepository.Update(user);
        }

        await unitOfWork.CompleteAsync();
    }

    public async Task<User> Handle(UpdateUserEmailCommand command)
    {
        var user = await userRepository.FindByIdAsync(command.UserId)
            ?? throw new Exception("User not found");

        if (userRepository.ExistsByEmail(command.Email) && user.Email != command.Email)
            throw new Exception($"Email {command.Email} is already taken");

        user.UpdateEmail(command.Email);
        userRepository.Update(user);
        await unitOfWork.CompleteAsync();
        return user;
    }

    public async Task Handle(DeleteUserCommand command)
    {
        var user = await userRepository.FindByIdAsync(command.UserId)
            ?? throw new Exception("User not found");

        if (user.Role == UserRole.Admin)
            throw new InvalidOperationException("Cannot delete an admin account.");

        userRepository.Remove(user);
        await unitOfWork.CompleteAsync();

        await SafePublishAsync(new UserDeletedEvent
        {
            UserId = user.Id,
            Email  = user.Email
        });
    }

    public async Task Handle(RegisterDeviceTokenCommand command)
    {
        var user = await userRepository.FindByIdAsync(command.UserId)
            ?? throw new Exception("User not found");

        user.UpdateDeviceToken(command.Token);
        userRepository.Update(user);
        await unitOfWork.CompleteAsync();

        await SafePublishAsync(new UserDeviceTokenUpdatedEvent
        {
            UserId = user.Id,
            Token  = command.Token,
            Role   = user.Role
        });
    }

    /// <summary>
    /// Publish an event but never crash the calling operation if the broker is
    /// unreachable. The state change has already been persisted; eventing is
    /// best-effort.
    /// </summary>
    private async Task SafePublishAsync<T>(T evt) where T : class
    {
        try
        {
            await publishEndpoint.Publish(evt);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to publish {EventType}. Continuing without eventing.", typeof(T).Name);
        }
    }
}
