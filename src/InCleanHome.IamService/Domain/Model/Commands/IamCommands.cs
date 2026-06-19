namespace InCleanHome.IamService.Domain.Model.Commands;

public record DeleteUserCommand(int UserId);
public record RegisterDeviceTokenCommand(int UserId, string? Token);
public record UpdateUserEmailCommand(int UserId, string Email);
public record UploadWorkerDocumentCommand(int UserId, string DocumentType, string FileName, string FileBase64);
public record VerifyUserCommand(int UserId);
public record ApproveWorkerDocumentsCommand(int UserId);
public record RejectWorkerDocumentsCommand(int UserId);
public record SuspendUserCommand(int UserId, TimeSpan Duration, string Reason);
public record ClearUserSuspensionCommand(int UserId);
