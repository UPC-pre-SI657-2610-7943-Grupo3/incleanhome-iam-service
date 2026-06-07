using InCleanHome.IamService.Domain.Model.Aggregates;
using InCleanHome.IamService.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace InCleanHome.IamService.Infrastructure.Persistence.Repositories;

public class WorkerDocumentRepository(IamDbContext context)
    : BaseRepository<WorkerDocument>(context), IWorkerDocumentRepository
{
    public async Task<IEnumerable<WorkerDocument>> FindByUserIdAsync(int userId)
        => await Context.Set<WorkerDocument>().Where(d => d.UserId == userId).ToListAsync();
}
