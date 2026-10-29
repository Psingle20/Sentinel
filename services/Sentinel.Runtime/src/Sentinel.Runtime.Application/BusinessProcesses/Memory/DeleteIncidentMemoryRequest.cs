using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Runtime.Application.BusinessProcesses.Memory;

/// <summary>An operator deletes one incident's memory for privacy.</summary>
public sealed record DeleteIncidentMemoryRequest(IncidentId IncidentId, string Actor, string Reason) : IRequest;
