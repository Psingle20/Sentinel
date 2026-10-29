using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Runtime.Application.BusinessTasks.Commands.Memory;

public sealed record DeleteIncidentMemoryCommand(IncidentId IncidentId, string Actor, string Reason) : ICommand;
