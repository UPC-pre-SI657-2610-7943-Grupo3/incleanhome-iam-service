using Microsoft.Extensions.Options;

namespace InCleanHome.IamService.Discovery;

/// <summary>
/// Hosted service that registers this service in Consul AFTER the web host
/// is fully started (Kestrel listening), and deregisters it on graceful
/// shutdown.
/// <para>
/// Discovery is opt-in: setting <c>CONSUL_DISCOVERY_ENABLED=false</c>
/// disables registration entirely (no Consul calls are made).
/// </para>
/// </summary>
public class ConsulRegistrationHostedService : IHostedService
{
    private readonly ConsulServiceRegistration _registration;
    private readonly ConsulRegistrationOptions _options;
    private readonly ILogger<ConsulRegistrationHostedService> _logger;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly bool _enabled;

    public ConsulRegistrationHostedService(
        ConsulServiceRegistration registration,
        IOptions<ConsulRegistrationOptions> options,
        ILogger<ConsulRegistrationHostedService> logger,
        IHostApplicationLifetime lifetime,
        IConfiguration configuration)
    {
        _registration = registration;
        _options = options.Value;
        _logger = logger;
        _lifetime = lifetime;

        // Discovery is opt-in. Defaults to true. Accept "false" or "0" to disable.
        var raw = configuration["CONSUL_DISCOVERY_ENABLED"]
                  ?? Environment.GetEnvironmentVariable("CONSUL_DISCOVERY_ENABLED")
                  ?? "true";
        _enabled = !string.Equals(raw, "false", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(raw, "0", StringComparison.OrdinalIgnoreCase);
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_enabled)
        {
            _logger.LogInformation(
                "[Consul] Discovery is DISABLED (CONSUL_DISCOVERY_ENABLED=false). " +
                "Service will not register itself.");
            return Task.CompletedTask;
        }

        // Register only AFTER the application is fully started (Kestrel listening,
        // /health endpoint responsive). Otherwise Consul's first health check would
        // hit a closed port and immediately flag the service as critical.
        _lifetime.ApplicationStarted.Register(async () =>
        {
            try
            {
                await _registration.RegisterAsync(_options, CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "[Consul] Registration failed. The gateway will still serve traffic but " +
                    "will not appear in the Consul service catalog.");
            }
        });

        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (!_enabled)
        {
            return;
        }

        await _registration.DeregisterAsync(_options.ConsulAddress, _options.ServiceId, cancellationToken);
    }
}
