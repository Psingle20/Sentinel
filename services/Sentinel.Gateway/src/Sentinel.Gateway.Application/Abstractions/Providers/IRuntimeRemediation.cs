using Sentinel.Contracts.Remediation;

namespace Sentinel.Gateway.Application.Abstractions.Providers;

/// <summary>
/// Runtime's authority over the remediation lifecycle, as a bounded request (ADR-006, ADR-012): a
/// revocation is recorded at the Gateway only once Runtime accepted it, because only Runtime knows
/// whether execution already passed the point of no return.
/// </summary>
public interface IRuntimeRemediation
{
    /// <summary>Runtime's answer; throws <see cref="HttpRequestException"/> when Runtime cannot be reached, which the caller treats as refusal.</summary>
    Task<RevocationResponse> RevokeAsync(RecordRevocation revocation, CancellationToken cancellationToken);
}
