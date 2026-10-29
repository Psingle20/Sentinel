namespace Sentinel.Gateway.Application.Options;

/// <summary>Configured as <c>Gateway:Approvals</c>.</summary>
public sealed class ApprovalOptions
{
    public const string Section = "Gateway:Approvals";

    /// <summary>How long an attestation is valid; Executor refuses it afterwards.</summary>
    public TimeSpan AttestationLifetime { get; set; } = TimeSpan.FromMinutes(15);
}