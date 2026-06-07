using InCleanHome.IamService.Domain.Model.Aggregates;
using InCleanHome.IamService.Domain.Model.Commands;

namespace InCleanHome.IamService.Domain.Services;

/// <summary>
/// Commands handler interface for the IAM bounded context.
/// </summary>
public interface IUserCommandService
{
    Task Handle(VerifyUserCommand command);
    Task Handle(ApproveWorkerDocumentsCommand command);
    Task Handle(RejectWorkerDocumentsCommand command);
    Task Handle(SuspendUserCommand command);
    Task Handle(ClearUserSuspensionCommand command);
    Task Handle(UploadWorkerDocumentCommand command);
    Task<User> Handle(UpdateUserEmailCommand command);
    Task Handle(DeleteUserCommand command);
    Task Handle(RegisterDeviceTokenCommand command);
}
