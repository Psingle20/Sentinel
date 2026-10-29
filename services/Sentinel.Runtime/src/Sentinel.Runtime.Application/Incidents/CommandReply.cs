using System.Text.Json.Serialization;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Runtime.Domain.Incidents;

namespace Sentinel.Runtime.Application.Incidents;

/// <summary>The writer's answer: whether new events were appended, and the state after the command.</summary>
public sealed record CommandReply(bool Changed, IncidentSnapshot Snapshot);
