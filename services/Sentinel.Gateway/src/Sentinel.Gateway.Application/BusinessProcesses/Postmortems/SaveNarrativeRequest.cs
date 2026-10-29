using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Contracts.Incidents;
using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Gateway.Application.BusinessProcesses.Postmortems;

/// <summary>A responder's edit of a postmortem narrative, over the version it was based on.</summary>
public sealed record SaveNarrativeRequest(IncidentId IncidentId, string Markdown, int ExpectedVersion, string EditorId) : IRequest<PostmortemNarrative>;
