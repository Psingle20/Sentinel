namespace Sentinel.Gateway.Application.Options;

/// <summary>Configured as <c>Gateway:Approvals</c>.</summary>
public sealed class ApprovalOptions
{
    public const string Section = "Gateway:Approvals";

    /// <summary>How long a decision attestation is valid; Executor and Runtime refuse it afterwards.</summary>
    public TimeSpan AttestationLifetime { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>How long an operator attestation is valid: long enough for one request to the owning service, no longer.</summary>
    public TimeSpan OperatorAttestationLifetime { get; set; } = TimeSpan.FromMinutes(2);
}
