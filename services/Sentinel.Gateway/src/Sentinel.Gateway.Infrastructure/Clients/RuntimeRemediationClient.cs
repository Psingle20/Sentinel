using System.Net.Http.Json;
using Sentinel.Contracts.Messaging;
using Sentinel.Contracts.Remediation;
using Sentinel.Gateway.Application.Abstractions.Providers;

namespace Sentinel.Gateway.Infrastructure.Clients;

/// <summary>
/// Runtime's revocation endpoint (ADR-006): a bounded request whose answer says whether the incident's
/// writer accepted the revocation. A non-success status is an <see cref="HttpRequestException"/>, which
/// the handler reports as Runtime being unavailable, leaving the approval standing.
/// </summary>
internal sealed class RuntimeRemediationClient(HttpClient http) : IRuntimeRemediation
{
    public const string RevocationsPath = "/v1/remediation/revocations";

    public async Task<RevocationResponse> RevokeAsync(RecordRevocation revocation, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await http.PostAsJsonAsync(new Uri(RevocationsPath, UriKind.Relative), revocation, SentinelJson.Options, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<RevocationResponse>(SentinelJson.Options, cancellationToken)
            ?? new RevocationResponse(false, "Runtime answered without a body.");
    }
}
