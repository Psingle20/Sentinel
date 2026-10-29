using Sentinel.Executor.Domain.Policy;

namespace Sentinel.Executor.Api.Contracts;

/// <summary>A new policy version, the version it replaces and who publishes it.</summary>
public sealed record PublishPolicyBody(PolicyDocument Document, string ExpectedVersion, string Author);
