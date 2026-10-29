using Microsoft.Extensions.Configuration;

namespace Sentinel.Executor.Infrastructure.DependencyInjection;

internal static class ConfigurationValues
{
    public static Uri RequiredUri(IConfiguration configuration, string key) =>
        new(configuration[key] ?? throw new InvalidOperationException($"{key} is not configured."));
}
