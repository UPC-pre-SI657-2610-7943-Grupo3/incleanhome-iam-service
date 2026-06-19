namespace InCleanHome.IamService.Infrastructure.ExternalServices.Auth0;

/// <summary>
/// Auth0 configuration. Read from Consul KV (config/iam-service.json).
/// </summary>
public class Auth0Settings
{
    public bool   Enabled  { get; set; } = false;
    public string Domain   { get; set; } = string.Empty;
    public string Audience { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;
}
