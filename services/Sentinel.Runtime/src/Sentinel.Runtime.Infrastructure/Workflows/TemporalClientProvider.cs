using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sentinel.Runtime.Application;
using Sentinel.Runtime.Application.Incidents;
using Temporalio.Api.Enums.V1;
using Temporalio.Client;
using Temporalio.Exceptions;
using Temporalio.Extensions.OpenTelemetry;
using Temporalio.Worker;
using Sentinel.Runtime.Application.Abstractions.Runtime;

namespace Sentinel.Runtime.Infrastructure.Workflows;

/// <summary>
/// One Temporal client for the process, connected on first use. A failed connection is not cached,
/// so callers fail fast while Temporal is down and succeed as soon as it is back; message handlers
/// that need it are retried by the event transport.
/// </summary>
public sealed class TemporalClientProvider(IOptions<TemporalOptions> options, ILoggerFactory loggers) : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private ITemporalClient? _client;

    public bool IsConnected => _client is not null;

    public async Task<ITemporalClient> GetAsync(CancellationToken cancellationToken)
    {
        if (_client is { } connected)
        {
            return connected;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            _client ??= await TemporalClient.ConnectAsync(new TemporalClientConnectOptions(options.Value.Address)
            {
                Namespace = options.Value.Namespace,
                LoggerFactory = loggers,
                Interceptors = [new TracingInterceptor()]
            });
            return _client;
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose() => _gate.Dispose();
}
