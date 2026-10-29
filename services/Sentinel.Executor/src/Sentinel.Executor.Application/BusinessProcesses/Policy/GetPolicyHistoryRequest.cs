using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Executor.Application.Abstractions.Persistence;

namespace Sentinel.Executor.Application.BusinessProcesses.Policy;

/// <summary>Every published policy version, newest first, with the repository's seed last.</summary>
public sealed record GetPolicyHistoryRequest : IRequest<IReadOnlyList<PolicyVersionInfo>>;
