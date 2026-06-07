using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace InCleanHome.IamService.Infrastructure.ExternalServices.Auth0;

public interface IAuth0Service
{
    bool IsEnabled { get; }
    Task<Auth0UserInfo?> ValidateAndGetUserInfoAsync(string accessToken);
}

public record Auth0UserInfo(string Sub, string Email, string Name, string? Picture);

/// <summary>
/// Auth0 integration.
///   1. Frontend logs in via Auth0 Universal Login and gets an access_token (RS256 JWT).
///   2. Frontend POSTs the token to /api/v1/auth/auth0/login.
///   3. We validate the JWT against the JWKS public keys of the tenant.
///   4. If valid, we GET /userinfo to fetch email/name/picture.
///   5. The caller decides whether to create/look up the local user and issue an internal JWT.
/// </summary>
public class Auth0Service : IAuth0Service
{
    private readonly Auth0Settings _settings;
    private readonly HttpClient _http;
    private static readonly JwtSecurityTokenHandler _jwtHandler = new();

    // Simple in-memory cache for the JWKS (refreshed every 6 hours).
    private static JsonWebKeySet? _jwksCache;
    private static DateTime _jwksCacheExpiresAt = DateTime.MinValue;
    private static readonly SemaphoreSlim _jwksLock = new(1, 1);

    public Auth0Service(IOptions<Auth0Settings> options, IHttpClientFactory httpFactory)
    {
        _settings = options.Value;
        _http     = httpFactory.CreateClient("auth0");
    }

    public bool IsEnabled => _settings.Enabled && !string.IsNullOrWhiteSpace(_settings.Domain);

    public async Task<Auth0UserInfo?> ValidateAndGetUserInfoAsync(string accessToken)
    {
        if (!IsEnabled || string.IsNullOrWhiteSpace(accessToken)) return null;

        try
        {
            var jwks = await GetJwksAsync();
            var validationParameters = new TokenValidationParameters
            {
                ValidateIssuer           = true,
                ValidIssuer              = $"https://{_settings.Domain}/",
                ValidateAudience         = !string.IsNullOrWhiteSpace(_settings.Audience),
                ValidAudience            = _settings.Audience,
                ValidateIssuerSigningKey = true,
                IssuerSigningKeys        = jwks.Keys,
                ValidateLifetime         = true,
                ClockSkew                = TimeSpan.FromMinutes(2)
            };
            _jwtHandler.ValidateToken(accessToken, validationParameters, out _);
        }
        catch (Exception e)
        {
            Console.WriteLine($"[Auth0] Token validation failed: {e.Message}");
            return null;
        }

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, $"https://{_settings.Domain}/userinfo");
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
            var resp = await _http.SendAsync(req);
            if (!resp.IsSuccessStatusCode) return null;

            var json  = await resp.Content.ReadFromJsonAsync<JsonElement>();
            var sub   = json.GetProperty("sub").GetString() ?? string.Empty;
            var email = json.TryGetProperty("email", out var e1)   ? e1.GetString() ?? string.Empty : string.Empty;
            var name  = json.TryGetProperty("name",  out var e2)   ? e2.GetString() ?? email        : email;
            var pic   = json.TryGetProperty("picture", out var e3) ? e3.GetString() : null;

            if (string.IsNullOrWhiteSpace(email)) return null;

            return new Auth0UserInfo(sub, email, name, pic);
        }
        catch (Exception e)
        {
            Console.WriteLine($"[Auth0] /userinfo failed: {e.Message}");
            return null;
        }
    }

    private async Task<JsonWebKeySet> GetJwksAsync()
    {
        if (_jwksCache is not null && _jwksCacheExpiresAt > DateTime.UtcNow)
            return _jwksCache;

        await _jwksLock.WaitAsync();
        try
        {
            if (_jwksCache is not null && _jwksCacheExpiresAt > DateTime.UtcNow)
                return _jwksCache;

            var url = $"https://{_settings.Domain}/.well-known/jwks.json";
            var jwksJson = await _http.GetStringAsync(url);
            _jwksCache = new JsonWebKeySet(jwksJson);
            _jwksCacheExpiresAt = DateTime.UtcNow.AddHours(6);
            return _jwksCache;
        }
        finally { _jwksLock.Release(); }
    }
}
