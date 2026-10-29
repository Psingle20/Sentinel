using System.Collections.Concurrent;
using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Integration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Sentinel.Contracts.Incidents;
using Sentinel.Contracts.Investigations;
using Sentinel.Contracts.Messaging;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Contracts.Remediation;
using Sentinel.Runtime.Domain.Incidents;
using ContractRecoveryEvaluated = Sentinel.Contracts.Incidents.RecoveryEvaluated;
using DomainRecoveryEvaluated = Sentinel.Runtime.Domain.Incidents.Events.RecoveryEvaluated;
using Sentinel.Runtime.Application.Abstractions.Runtime;
using Sentinel.Runtime.Application.Abstractions.Persistence;

namespace Sentinel.Runtime.Application.Incidents;

/// <summary>An incident in memory with the stored events it was built from; what IncidentGrain keeps between commands.</summary>
public sealed record LoadedIncident(Incident Incident, IReadOnlyList<StoredEvent> History);
