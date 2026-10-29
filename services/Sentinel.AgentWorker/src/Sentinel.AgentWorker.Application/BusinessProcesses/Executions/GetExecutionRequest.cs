using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.AgentWorker.Domain.Executions;
using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.AgentWorker.Application.BusinessProcesses.Executions;

/// <summary>One agent execution's audit record; nothing when it is unknown.</summary>
public sealed record GetExecutionRequest(AgentExecutionId ExecutionId) : IRequest<AgentExecutionRecord?>;
