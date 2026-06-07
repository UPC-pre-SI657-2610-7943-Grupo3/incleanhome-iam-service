using InCleanHome.IamService.Domain.Model.Aggregates;
using InCleanHome.IamService.Domain.Model.Queries;

namespace InCleanHome.IamService.Domain.Services;

public interface IUserQueryService
{
    Task<User?> Handle(GetUserByIdQuery query);
    Task<User?> Handle(GetUserByEmailQuery query);
    Task<IEnumerable<User>> Handle(GetAllUsersQuery query);
    Task<IEnumerable<WorkerDocument>> Handle(GetWorkerDocumentsByUserIdQuery query);
}
