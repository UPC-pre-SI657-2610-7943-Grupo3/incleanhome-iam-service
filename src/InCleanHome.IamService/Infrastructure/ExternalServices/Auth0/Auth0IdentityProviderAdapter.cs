using System.Net.Http.Json;
using System.Text.Json;
using InCleanHome.IamService.Domain.Services.External;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace InCleanHome.IamService.Infrastructure.ExternalServices.Auth0;

/// <summary>
/// Concrete adapter for Auth0 implementing <see cref="IIdentityProvider"/>.
/// Validates RS256 JWTs against the JWKS public keys and fetches /userinfo.
/// </summary>
public class Auth0IdentityProviderAdapter : IIdentityProvider
{
    private readonly Auth0Settings _settings;
    private readonly HttpClient _http;

    private static JsonWebKeySet? _jwksCache;
    private static DateTime _jwksCacheExpiresAt = DateTime.MinValue;
    private static readonly SemaphoreSlim _jwksLock = new(1, 1);

    public Auth0IdentityProviderAdapter(IOptions<Auth0Settings> options, IHttpClientFactory httpFactory)
    {
        _settings = options.Value;
        _http = httpFactory.CreateClient("auth0");
    }

    public bool IsEnabled => _settings.Enabled && !string.IsNullOrWhiteSpace(_settings.Domain);

    public async Task<IdentityProviderUserInfo?> ValidateAndGetUserInfoAsync(string accessToken)
    {
        if (!IsEnabled)
        {
            Console.WriteLine("[Auth0] Disabled — refusing to validate token.");
            return null;
        }
        if (string.IsNullOrWhiteSpace(accessToken)) return null;

        // 1) Validate signature against JWKS.
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

            var handler = new JsonWebTokenHandler();
            var result = await handler.ValidateTokenAsync(accessToken, validationParameters);
            if (!result.IsValid)
            {
                Console.WriteLine($"[Auth0] Token invalid: {result.Exception?.Message}");
                return null;
            }
        }
        catch (Exception e)
        {
            Console.WriteLine($"[Auth0] Token validation failed: {e.Message}");
            return null;
        }

        // 2) Fetch /userinfo for email/name/picture.
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

            return new IdentityProviderUserInfo(sub, email, name, pic);
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
