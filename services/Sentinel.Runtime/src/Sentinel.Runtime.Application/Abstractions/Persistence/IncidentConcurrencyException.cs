using CleanArchitecture.BuildingBlocks;
using Sentinel.Contracts.Incidents;
using Sentinel.Contracts.Observations;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Contracts.Queries;
using Sentinel.Contracts.Remediation;
using Sentinel.Runtime.Application.Incidents;
using Sentinel.Runtime.Domain.Correlation;
using Sentinel.Runtime.Domain.Incidents;

namespace Sentinel.Runtime.Application.Abstractions.Persistence;

/// <summary>Raised when another writer appended to the stream first; the operation is retried from a fresh read.</summary>
public sealed class IncidentConcurrencyException(IncidentId incidentId, long expectedVersion)
    : Exception($"Incident {incidentId} moved past version {expectedVersion} while this change was being made.");
