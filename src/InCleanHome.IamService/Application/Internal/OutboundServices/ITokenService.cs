using InCleanHome.IamService.Domain.Model.Aggregates;

namespace InCleanHome.IamService.Application.Internal.OutboundServices;

public interface ITokenService
{
    string GenerateToken(User user);
    Task<int?> ValidateToken(string token);
}
