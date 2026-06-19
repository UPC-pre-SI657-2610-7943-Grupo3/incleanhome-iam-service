using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace InCleanHome.IamService.Infrastructure.ExternalServices.ProfileService;

/// <summary>
/// HTTP client to talk to Profile Service. Used by the Auth0 / authentication
/// flow so that the IAM Service can:
///   1. Fetch name/phone of a user to include in /me responses (chatty but
///      keeps the frontend contract identical to the monolith).
///   2. Create a Profile right after creating the User during
///      /auth0/complete-registration.
///
/// If Profile Service is unreachable, methods return null. Callers decide
/// whether to fail or degrade gracefully.
/// </summary>
public interface IProfileServiceClient
{
    Task<ProfileSummary?> GetClientProfileAsync(int userId, string? bearerToken = null);
    Task<ProfileSummary?> GetWorkerProfileAsync(int userId, string? bearerToken = null);
    Task<bool> CreateClientProfileAsync(int userId, string name, string phone, string bearerToken);
    Task<bool> CreateWorkerProfileAsync(
        int userId,
        string name,
        string phone,
        int age,
        string gender,
        List<string> serviceTypes,
        List<string> zones,
        decimal hourlyRate,
        int experienceYears,
        string bio,
        string bearerToken);
}

public record ProfileSummary(string Name, string? Phone);

public class ProfileServiceClient(
    HttpClient http,
    IConfiguration configuration,
    ILogger<ProfileServiceClient> logger) : IProfileServiceClient
{
    private string BaseUrl => configuration["Dependencies:ProfileServiceUrl"]
                              ?? "http://profile-service:5002";

    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    public async Task<ProfileSummary?> GetClientProfileAsync(int userId, string? bearerToken = null)
        => await TryGetProfile($"{BaseUrl}/api/v1/profiles/clients/{userId}", bearerToken);

    public async Task<ProfileSummary?> GetWorkerProfileAsync(int userId, string? bearerToken = null)
        => await TryGetProfile($"{BaseUrl}/api/v1/profiles/workers/{userId}", bearerToken);

    private async Task<ProfileSummary?> TryGetProfile(string url, string? bearerToken)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            if (!string.IsNullOrEmpty(bearerToken))
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);

            using var resp = await http.SendAsync(req);
            if (!resp.IsSuccessStatusCode) return null;

            var json = await resp.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
            var name  = json.TryGetProperty("name",  out var n) ? n.GetString() ?? string.Empty : string.Empty;
            var phone = json.TryGetProperty("phone", out var p) ? p.GetString() : null;
            return new ProfileSummary(name, phone);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "GET {Url} failed", url);
            return null;
        }
    }

    public async Task<bool> CreateClientProfileAsync(int userId, string name, string phone, string bearerToken)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/api/v1/profiles/clients");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
            req.Content = JsonContent.Create(new { userId, name, phone }, options: JsonOptions);

            using var resp = await http.SendAsync(req);
            if (resp.IsSuccessStatusCode) return true;

            var body = await resp.Content.ReadAsStringAsync();
            logger.LogError("POST /api/v1/profiles/clients failed: {Status} {Body}", resp.StatusCode, body);
            return false;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "POST /api/v1/profiles/clients threw");
            return false;
        }
    }

    public async Task<bool> CreateWorkerProfileAsync(
        int userId, string name, string phone, int age, string gender,
        List<string> serviceTypes, List<string> zones, decimal hourlyRate,
        int experienceYears, string bio, string bearerToken)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/api/v1/profiles/workers");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
            req.Content = JsonContent.Create(new
            {
                userId, name, phone, age, gender,
                serviceTypes, zones, hourlyRate, experienceYears, bio
            }, options: JsonOptions);

            using var resp = await http.SendAsync(req);
            if (resp.IsSuccessStatusCode) return true;

            var body = await resp.Content.ReadAsStringAsync();
            logger.LogError("POST /api/v1/profiles/workers failed: {Status} {Body}", resp.StatusCode, body);
            return false;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "POST /api/v1/profiles/workers threw");
            return false;
        }
    }
}
