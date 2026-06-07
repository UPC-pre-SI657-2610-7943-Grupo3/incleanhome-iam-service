using InCleanHome.IamService.Domain.Model.Aggregates;
using InCleanHome.IamService.Domain.Model.Queries;
using InCleanHome.IamService.Domain.Repositories;
using InCleanHome.IamService.Domain.Services;

namespace InCleanHome.IamService.Application.Internal.QueryServices;

public class UserQueryService(
    IUserRepository userRepository,
    IWorkerDocumentRepository workerDocumentRepository) : IUserQueryService
{
    public async Task<User?> Handle(GetUserByIdQuery query)        => await userRepository.FindByIdAsync(query.Id);
    public async Task<User?> Handle(GetUserByEmailQuery query)     => await userRepository.FindByEmailAsync(query.Email);
    public async Task<IEnumerable<User>> Handle(GetAllUsersQuery query) => await userRepository.ListAsync();

    public async Task<IEnumerable<WorkerDocument>> Handle(GetWorkerDocumentsByUserIdQuery query)
        => await workerDocumentRepository.FindByUserIdAsync(query.UserId);
}
