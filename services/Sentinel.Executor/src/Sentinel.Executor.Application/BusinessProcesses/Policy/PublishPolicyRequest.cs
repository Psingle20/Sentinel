using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Executor.Domain.Policy;

namespace Sentinel.Executor.Application.BusinessProcesses.Policy;

/// <summary>An operator publishes a new policy version over the one they started from (Phase 08).</summary>
public sealed record PublishPolicyRequest(PolicyDocument Document, string Author, string ExpectedVersion) : IRequest<PolicyDocument>;
