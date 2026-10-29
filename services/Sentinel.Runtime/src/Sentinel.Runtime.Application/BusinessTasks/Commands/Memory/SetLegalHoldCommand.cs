using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Contracts.Incidents;
using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Runtime.Application.BusinessTasks.Commands.Memory;

public sealed record SetLegalHoldCommand(IncidentId IncidentId, bool Hold, string Actor) : ICommand<IncidentMemoryView?>;
