namespace Sentinel.Gateway.Api;

public static class Roles
{
    /// <summary>Reads incidents of the caller's tenants and writes postmortem narratives.</summary>
    public const string Responder = "responder";

    /// <summary>Approves, rejects and revokes remediation plans.</summary>
    public const string Approver = "approver";

    /// <summary>Changes the control plane: policy, operator controls and sources.</summary>
    public const string Operator = "operator";
}