using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Runtime.Application.Memory;

namespace Sentinel.Runtime.Application.BusinessTasks.Commands.Memory;

public sealed record PurgeIncidentMemoryCommand(bool DryRun, string Actor, int Limit) : ICommand<PurgeReport>;
