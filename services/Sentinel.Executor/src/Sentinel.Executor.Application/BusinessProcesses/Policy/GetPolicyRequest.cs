using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Executor.Domain.Policy;

namespace Sentinel.Executor.Application.BusinessProcesses.Policy;

/// <summary>The current remediation policy document.</summary>
public sealed record GetPolicyRequest : IRequest<PolicyDocument>;
