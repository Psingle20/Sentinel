using CleanArchitecture.BuildingBlocks;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Runtime.Application.Incidents;

namespace Sentinel.Runtime.Application.Abstractions.Runtime;

/// <summary>
/// The incident's single writer (ADR-004): IncidentGrain in a running Runtime, an in-process lock in
/// tests. It serializes every command for one incident and appends with optimistic concurrency.
/// </summary>
public interface IIncidentOwner
{
    Task<Result<CommandReply>> SendAsync(IncidentId incidentId, IncidentCommand command, CancellationToken cancellationToken);

    /// <summary>The authoritative state, read from the stream rather than the projection.</summary>
    Task<IncidentSnapshot?> GetAsync(IncidentId incidentId, CancellationToken cancellationToken);
}