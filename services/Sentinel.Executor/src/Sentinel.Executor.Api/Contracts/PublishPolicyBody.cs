using Sentinel.Executor.Domain.Policy;

namespace Sentinel.Executor.Api.Contracts;

/// <summary>A new policy version and the version it replaces. <see cref="Author"/> is informational: the operator recorded is the one the attestation names.</summary>
public sealed record PublishPolicyBody(PolicyDocument Document, string ExpectedVersion, string Author);
