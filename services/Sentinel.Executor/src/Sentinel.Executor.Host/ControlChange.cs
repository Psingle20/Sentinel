using Sentinel.Executor.Domain.Policy;

namespace Sentinel.Executor.Host;

/// <summary>An operator's change to a control: whether it is engaged, and who changed it.</summary>
public sealed record ControlChange(bool Enabled, string Actor);

/// <summary>A new policy version, the version it replaces and who publishes it.</summary>
public sealed record PublishPolicyRequest(PolicyDocument Document, string ExpectedVersion, string Author);
