using InCleanHome.IamService.Application.Internal.OutboundServices;
using InCleanHome.IamService.Domain.Model.Aggregates;
using InCleanHome.IamService.Domain.Model.Commands;
using InCleanHome.IamService.Domain.Model.ValueObjects;
using InCleanHome.IamService.Domain.Repositories;
using InCleanHome.IamService.Domain.Services;

namespace InCleanHome.IamService.Application.Internal.CommandServices;

/// <summary>
/// Command handler for the IAM bounded context.
/// </summary>
public class UserCommandService(
    IUserRepository userRepository,
    IWorkerDocumentRepository workerDocumentRepository,
    IHashingService hashingService,
    ITokenService tokenService,
    IUnitOfWork unitOfWork) : IUserCommandService
{
    // Administrative commands (migrated from monolith)
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
    }

    public async Task Handle(SuspendUserCommand command)
    {
        var user = await userRepository.FindByIdAsync(command.UserId)
            ?? throw new Exception($"User {command.UserId} not found");
        user.Suspend(command.Duration, command.Reason);
        userRepository.Update(user);
        await unitOfWork.CompleteAsync();
    }

    public async Task Handle(ClearUserSuspensionCommand command)
    {
        var user = await userRepository.FindByIdAsync(command.UserId)
            ?? throw new Exception($"User {command.UserId} not found");
        user.ClearSuspension();
        userRepository.Update(user);
        await unitOfWork.CompleteAsync();
    }

    public async Task Handle(UploadWorkerDocumentCommand command)
    {
        if (!DocumentType.IsValid(command.DocumentType))
            throw new Exception("Invalid document type");

        var user = await userRepository.FindByIdAsync(command.UserId)
            ?? throw new Exception("User not found");

        if (user.Role != UserRole.Worker)
            throw new Exception("Only workers can upload documents");

        // Replace any existing document of the same type so we keep at most one
        // per document_type per worker.
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
    }

    public async Task Handle(RegisterDeviceTokenCommand command)
    {
        var user = await userRepository.FindByIdAsync(command.UserId)
            ?? throw new Exception("User not found");

        user.UpdateDeviceToken(command.Token);
        userRepository.Update(user);
        await unitOfWork.CompleteAsync();
    }
}
