using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Contracts.Incidents;
using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Gateway.Application.BusinessTasks.Commands.Postmortems;

public sealed record SaveNarrativeCommand(IncidentId IncidentId, string Markdown, int ExpectedVersion, string EditorId) : ICommand<PostmortemNarrative>;
