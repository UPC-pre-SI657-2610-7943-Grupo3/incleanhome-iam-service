using InCleanHome.IamService.Domain.Model.Aggregates;
using InCleanHome.IamService.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace InCleanHome.IamService.Infrastructure.Persistence.Repositories;

public class UserRepository(IamDbContext context) : BaseRepository<User>(context), IUserRepository
{
    public async Task<User?> FindByEmailAsync(string email)
        => await Context.Set<User>().FirstOrDefaultAsync(u => u.Email == email);

    public bool ExistsByEmail(string email)
        => Context.Set<User>().Any(u => u.Email == email);

    public async Task<User?> FindByResetTokenAsync(string token)
        => await Context.Set<User>().FirstOrDefaultAsync(u => u.ResetToken == token);

    public async Task<string?> FindDeviceTokenByIdAsync(int userId)
        => await Context.Set<User>()
            .Where(u => u.Id == userId)
            .Select(u => u.DeviceToken)
            .FirstOrDefaultAsync();
}

public class WorkerDocumentRepository(IamDbContext context)
    : BaseRepository<WorkerDocument>(context), IWorkerDocumentRepository
{
    public async Task<IEnumerable<WorkerDocument>> FindByUserIdAsync(int userId)
        => await Context.Set<WorkerDocument>().Where(d => d.UserId == userId).ToListAsync();
}
