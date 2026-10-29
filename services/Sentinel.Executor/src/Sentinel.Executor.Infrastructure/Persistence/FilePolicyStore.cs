using System.Net.Http.Headers;
using System.Text.Json;
using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Infrastructure.Messaging;
using CleanArchitecture.BuildingBlocks.Infrastructure.PostgreSql;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using NpgsqlTypes;
using Sentinel.Contracts.Messaging;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Contracts.Remediation;
using Sentinel.Executor.Application;
using Sentinel.Executor.Domain.Capabilities;
using Sentinel.Executor.Domain.Executions;
using Sentinel.Executor.Domain.Policy;
using Sentinel.Executor.Application.Options;

namespace Sentinel.Executor.Infrastructure.Persistence;

/// <summary>
/// The remediation policy as code: a JSON document beside the host, versioned in the repository and
/// reloaded when the file changes, so a policy change needs no rebuild and every evaluation names
/// the version it used. A document that fails to load keeps the previous one; with none, every action is denied.
/// It seeds the policy until the control plane publishes a version.
/// </summary>
internal sealed class FilePolicyStore(IOptions<ExecutorOptions> options, Microsoft.Extensions.Hosting.IHostEnvironment environment, ILogger<FilePolicyStore> logger)
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };

    private static readonly PolicyDocument DenyAll = new() { Version = "deny-all", DefaultMode = AutonomyMode.ReadOnly };

    private readonly Lock _gate = new();
    private (DateTime Written, PolicyDocument Document)? _loaded;

    public PolicyDocument Current
    {
        get
        {
            string path = Path.Combine(environment.ContentRootPath, options.Value.PolicyFile);
            lock (_gate)
            {
                DateTime written = File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;
                if (_loaded is { } loaded && loaded.Written == written)
                {
                    return loaded.Document;
                }

                try
                {
                    PolicyDocument document = JsonSerializer.Deserialize<PolicyDocument>(File.ReadAllText(path), Json) ?? DenyAll;
                    _loaded = (written, document);
                    logger.LogInformation("Loaded remediation policy {Version} from {Path}", document.Version, path);
                    return document;
                }
                catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
                {
                    logger.LogError(exception, "The remediation policy at {Path} could not be loaded; keeping {Version}", path, _loaded?.Document.Version ?? DenyAll.Version);
                    return _loaded?.Document ?? DenyAll;
                }
            }
        }
    }
}
