namespace Sentinel.Contracts.Remediation;

/// <summary>What an operator attestation for a control change hashes: the control's resource path and the state requested; the actor comes from the claims.</summary>
public sealed record ControlChangeContent(string Resource, bool Enabled);
