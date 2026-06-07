using System.Text.Json;
using Serilog;

namespace InCleanHome.IamService.Configuration;

/// <summary>
/// Loads JSON configuration from Consul KV at startup time. The fetched
/// JSON is added as an in-memory configuration source so that any code
/// reading <see cref="IConfiguration"/> sees it transparently.
/// <para>
/// On failure (network error, Consul down, key not found), the loader
/// falls back to whatever sources were already in the builder
/// (typically appsettings.json).
/// </para>
/// </summary>
public static class ConsulConfigurationLoader
{
    /// <summary>
    /// Fetches config/&lt;serviceName&gt; from Consul KV and merges it into the
    /// application configuration. Retries with backoff to tolerate Consul/seeder
    /// being slow to come up.
    /// </summary>
    /// <returns>True if the config was loaded from Consul. False if fallback.</returns>
    public static async Task<bool> LoadFromConsulAsync(
        IConfigurationBuilder configBuilder,
        string consulAddress,
        string serviceName,
        CancellationToken cancellationToken = default)
    {
        var url = $"{consulAddress.TrimEnd('/')}/v1/kv/config/{serviceName}?raw";

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };

        const int maxAttempts = 6;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                var response = await http.GetAsync(url, cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync(cancellationToken);
                    using (JsonDocument.Parse(json)) { /* validates */ }

                    var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));
                    configBuilder.AddJsonStream(stream);

                    Log.Information(
                        "[Consul] Loaded config for '{ServiceName}' from {Url}",
                        serviceName, url);
                    return true;
                }

                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    Log.Warning(
                        "[Consul] Key 'config/{ServiceName}' not found in KV. " +
                        "Falling back to local appsettings.json.",
                        serviceName);
                    return false;
                }

                Log.Warning(
                    "[Consul] Attempt {Attempt}/{Max}: HTTP {StatusCode} from {Url}",
                    attempt, maxAttempts, (int)response.StatusCode, url);
            }
            catch (Exception ex) when (attempt < maxAttempts)
            {
                Log.Warning(
                    "[Consul] Attempt {Attempt}/{Max} failed: {Message}. Retrying...",
                    attempt, maxAttempts, ex.Message);
            }
            catch (Exception ex)
            {
                Log.Error(ex,
                    "[Consul] All {Max} attempts to load config from {Url} failed.",
                    maxAttempts, url);
                return false;
            }

            await Task.Delay(TimeSpan.FromSeconds(2 * attempt), cancellationToken);
        }

        Log.Error(
            "[Consul] Exhausted {Max} attempts to load config from {Url}. " +
            "Continuing with local appsettings.json.",
            maxAttempts, url);
        return false;
    }
}
