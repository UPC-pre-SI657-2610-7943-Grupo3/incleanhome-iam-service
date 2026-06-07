namespace InCleanHome.IamService.Infrastructure.ExternalServices.Auth0;

/// <summary>
/// Auth0 configuration. Read from Consul KV (config/iam-service.json).
/// If Enabled = false the Auth0 endpoints return 503 and the frontend should hide
/// the "Continue with Auth0" button. The client secret lives in env vars.
/// </summary>
public class Auth0Settings
{
    public bool   Enabled  { get; set; } = false;
    public string Domain   { get; set; } = string.Empty;
    public string Audience { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;
}
