namespace InCleanHome.IamService.Domain.Model.Queries;

public record GetUserByIdQuery(int Id);
public record GetUserByEmailQuery(string Email);
public record GetAllUsersQuery;
public record GetWorkerDocumentsByUserIdQuery(int UserId);
