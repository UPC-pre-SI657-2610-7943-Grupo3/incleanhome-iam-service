using InCleanHome.IamService.Domain.Model.Aggregates;

namespace InCleanHome.IamService.Domain.Repositories;

public interface IUserRepository : IBaseRepository<User>
{
    Task<User?> FindByEmailAsync(string email);
    bool ExistsByEmail(string email);
    Task<User?> FindByResetTokenAsync(string token);
    Task<string?> FindDeviceTokenByIdAsync(int userId);
}
