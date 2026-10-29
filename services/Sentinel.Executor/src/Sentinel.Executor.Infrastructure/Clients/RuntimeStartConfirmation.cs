using System.Buffers.Text;
using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Sentinel.Contracts.Messaging;
using Sentinel.Contracts.Remediation;
using Sentinel.Executor.Application;
using Sentinel.Executor.Domain.Capabilities;
using Sentinel.Executor.Application.Abstractions.Providers;

namespace Sentinel.Executor.Infrastructure.Clients;

/// <summary>Runtime's start confirmation over HTTP; any failure to get an answer throws, so execution fails closed.</summary>
internal sealed class RuntimeStartConfirmation(HttpClient http) : IStartConfirmation
{
    public async Task<RemediationStartResponse> ConfirmAsync(RemediationStartRequest request, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await http.PostAsJsonAsync("/v1/remediation/start-confirmations", request, SentinelJson.Options, cancellationToken);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<RemediationStartResponse>(SentinelJson.Options, cancellationToken))!;
    }
}
