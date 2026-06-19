using System.Security.Claims;
using System.Text;
using InCleanHome.IamService.Application.Internal.OutboundServices;
using InCleanHome.IamService.Domain.Model.Aggregates;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace InCleanHome.IamService.Infrastructure.Tokens;

public class TokenService(IOptions<TokenSettings> tokenSettings) : ITokenService
{
    private readonly TokenSettings _tokenSettings = tokenSettings.Value;

    public string GenerateToken(User user)
    {
        if (string.IsNullOrWhiteSpace(_tokenSettings.Secret))
            throw new InvalidOperationException("JWT signing key is not configured. Set JWT_SIGNING_KEY env var.");

        var key = Encoding.UTF8.GetBytes(_tokenSettings.Secret);
        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Issuer   = _tokenSettings.Issuer,
            Audience = _tokenSettings.Audience,
            Subject  = new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.Sid,  user.Id.ToString()),
                new Claim(ClaimTypes.Name, user.Email),
                new Claim(ClaimTypes.Role, user.Role)
            }),
            Expires = DateTime.UtcNow.AddDays(_tokenSettings.ExpirationDays),
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(key),
                SecurityAlgorithms.HmacSha256Signature)
        };

        return new JsonWebTokenHandler().CreateToken(tokenDescriptor);
    }

    public async Task<int?> ValidateToken(string token)
    {
        if (string.IsNullOrEmpty(token)) return null;
        if (string.IsNullOrWhiteSpace(_tokenSettings.Secret)) return null;

        var handler = new JsonWebTokenHandler();
        var key = Encoding.UTF8.GetBytes(_tokenSettings.Secret);
        try
        {
            var result = await handler.ValidateTokenAsync(token, new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey         = new SymmetricSecurityKey(key),
                ValidateIssuer           = true,
                ValidIssuer              = _tokenSettings.Issuer,
                ValidateAudience         = true,
                ValidAudience            = _tokenSettings.Audience,
                ValidateLifetime         = true,
                ClockSkew                = TimeSpan.FromMinutes(1)
            });

            if (!result.IsValid) return null;

            var jwt = (JsonWebToken)result.SecurityToken;
            var sid = jwt.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Sid)?.Value;
            return int.TryParse(sid, out var userId) ? userId : null;
        }
        catch
        {
            return null;
        }
    }
}
