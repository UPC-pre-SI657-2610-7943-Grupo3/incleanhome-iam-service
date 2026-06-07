using InCleanHome.IamService.Domain.Model.Aggregates;

namespace InCleanHome.IamService.Domain.Repositories;

public interface IWorkerDocumentRepository : IBaseRepository<WorkerDocument>
{
    Task<IEnumerable<WorkerDocument>> FindByUserIdAsync(int userId);
}
