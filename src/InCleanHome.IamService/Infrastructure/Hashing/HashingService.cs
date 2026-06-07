using InCleanHome.IamService.Application.Internal.OutboundServices;
using BCryptNet = BCrypt.Net.BCrypt;

namespace InCleanHome.IamService.Infrastructure.Hashing;

public class HashingService : IHashingService
{
    public string HashPassword(string password) => BCryptNet.HashPassword(password);
    public bool VerifyPassword(string password, string passwordHash) => BCryptNet.Verify(password, passwordHash);
}
