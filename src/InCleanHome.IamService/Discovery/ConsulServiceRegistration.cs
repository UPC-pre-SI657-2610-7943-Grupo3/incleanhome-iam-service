using System.Text;
using System.Text.Json;

namespace InCleanHome.IamService.Discovery;

/// <summary>
/// Minimal HTTP client to register and deregister this service with the
/// Consul agent. Uses the Consul HTTP API:
///   PUT  /v1/agent/service/register
///   PUT  /v1/agent/service/deregister/{id}
/// </summary>
public class ConsulServiceRegistration
{
    private readonly HttpClient _http;
    private readonly ILogger<ConsulServiceRegistration> _logger;

    public ConsulServiceRegistration(HttpClient http, ILogger<ConsulServiceRegistration> logger)
    {
        _http = http;
        _logger = logger;
    }

    public async Task RegisterAsync(ConsulRegistrationOptions options, CancellationToken cancellationToken)
    {
        var payload = new
        {
            ID = options.ServiceId,
            Name = options.ServiceName,
            Address = options.Host,
            Port = options.Port,
            Tags = options.Tags,
            Check = new
            {
                HTTP = options.HealthCheckUrl,
                Interval = options.HealthCheckIntervalSeconds + "s",
                Timeout = options.HealthCheckTimeoutSeconds + "s",
                DeregisterCriticalServiceAfter = options.DeregisterAfterMinutes + "m"
            }
        };

        var json = JsonSerializer.Serialize(payload);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var url = $"{options.ConsulAddress.TrimEnd('/')}/v1/agent/service/register";
        var response = await _http.PutAsync(url, content, cancellationToken);

        if (response.IsSuccessStatusCode)
        {
            _logger.LogInformation(
                "[Consul] Registered service '{ServiceName}' (id={ServiceId}) at {Host}:{Port} with health check {HealthUrl}",
                options.ServiceName, options.ServiceId, options.Host, options.Port, options.HealthCheckUrl);
        }
        else
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogError(
                "[Consul] Failed to register '{ServiceName}': HTTP {Status} - {Body}",
                options.ServiceName, (int)response.StatusCode, body);
        }
    }

    public async Task DeregisterAsync(string consulAddress, string serviceId, CancellationToken cancellationToken)
    {
        try
        {
            var url = $"{consulAddress.TrimEnd('/')}/v1/agent/service/deregister/{serviceId}";
            var response = await _http.PutAsync(url, null, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("[Consul] Deregistered service id={ServiceId}", serviceId);
            }
            else
            {
                _logger.LogWarning(
                    "[Consul] Failed to deregister id={ServiceId}: HTTP {Status}",
                    serviceId, (int)response.StatusCode);
            }
        }
        catch (Exception ex)
        {
            // Swallow on shutdown; we don't want to crash the shutdown process.
            _logger.LogWarning(ex, "[Consul] Exception during deregister of id={ServiceId}", serviceId);
        }
    }
}

public sealed class ConsulRegistrationOptions
{
    public required string ConsulAddress { get; init; }
    public required string ServiceName { get; init; }
    public required string ServiceId { get; init; }
    public required string Host { get; init; }
    public required int Port { get; init; }
    public string[] Tags { get; init; } = Array.Empty<string>();
    public required string HealthCheckUrl { get; init; }
    public int HealthCheckIntervalSeconds { get; init; } = 30;
    public int HealthCheckTimeoutSeconds { get; init; } = 5;
    public int DeregisterAfterMinutes { get; init; } = 2;
}
