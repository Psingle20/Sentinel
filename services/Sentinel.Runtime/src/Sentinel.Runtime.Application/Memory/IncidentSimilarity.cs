using System.Globalization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sentinel.Contracts.Incidents;
using Sentinel.Contracts.Investigations;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Contracts.Remediation;
using Sentinel.Runtime.Application.Incidents;
using Sentinel.Runtime.Domain.Incidents;
using Sentinel.Runtime.Application.Abstractions.Runtime;
using Sentinel.Runtime.Application.Abstractions.Persistence;
using Sentinel.Runtime.Application.Options;

namespace Sentinel.Runtime.Application.Memory;

/// <summary>
/// Similar-incident retrieval from incident memory (Phase 07), within tenant, environment, privacy and
/// retention boundaries: hints for a new investigation, and the similar incidents responders ask for.
/// </summary>
public sealed class IncidentSimilarity(IIncidentMemoryStore memory, IIncidentOwner owner, TimeProvider time, IOptions<RuntimeOptions> options)
{
    /// <summary>Hints for an investigation of an open incident: remembered incidents whose observable features resemble its own.</summary>
    public async Task<IReadOnlyList<PriorIncident>> HintsForAsync(IncidentSnapshot incident, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(incident);

        IReadOnlyList<SimilarIncident> similar = await SimilarAsync(incident.TenantId, incident.EnvironmentId, incident.IncidentId, incident.Features, observableOnly: true, cancellationToken);
        return [.. similar.Select(match => new PriorIncident(match.IncidentId, match.Similarity, match.SharedFeatures, match.CauseKey, match.CauseStatement, match.Outcome, match.RecordedAt))];
    }

    /// <summary>Incidents similar to a remembered or open one, compared on every feature it has.</summary>
    public async Task<IReadOnlyList<SimilarIncident>?> SimilarToAsync(IncidentId incidentId, CancellationToken cancellationToken)
    {
        if (await memory.GetAsync(incidentId, cancellationToken) is { Retention.DeletedAt: null } remembered)
        {
            return await SimilarAsync(remembered.TenantId, remembered.EnvironmentId, incidentId, remembered.Features, observableOnly: false, cancellationToken);
        }

        return await owner.GetAsync(incidentId, cancellationToken) is { } open
            ? await SimilarAsync(open.TenantId, open.EnvironmentId, incidentId, open.Features, observableOnly: true, cancellationToken)
            : null;
    }

    private async Task<IReadOnlyList<SimilarIncident>> SimilarAsync(TenantId tenantId, EnvironmentId environmentId, IncidentId self, IReadOnlyList<string> features, bool observableOnly, CancellationToken cancellationToken)
    {
        MemoryOptions settings = options.Value.Memory;
        string[] probe = [.. features.Where(feature => !observableOnly || MemoryFeatures.IsObservable(feature))];
        if (probe.Length == 0)
        {
            return [];
        }

        IReadOnlyList<IncidentMemoryView> candidates = await memory.CandidatesAsync(tenantId, settings.CrossEnvironment ? null : environmentId, probe, time.GetUtcNow(), 50, cancellationToken);
        return [.. candidates
            .Where(candidate => candidate.IncidentId != self && candidate.TenantId == tenantId && !settings.ExcludedFromRetrieval.Contains(candidate.Retention.PrivacyClass))
            .Select(candidate => (Candidate: candidate, Match: MemoryFeatures.Score(probe, candidate.Features, observableOnly)))
            .Where(scored => scored.Match.Score >= settings.MinimumSimilarity)
            .OrderByDescending(scored => scored.Match.Score)
            .ThenByDescending(scored => scored.Candidate.RecordedAt)
            .Take(settings.SimilarLimit)
            .Select(scored => new SimilarIncident(
                scored.Candidate.IncidentId,
                scored.Match.Score,
                scored.Match.Shared,
                scored.Candidate.Cause?.Key,
                scored.Candidate.Cause?.Statement,
                scored.Candidate.Status.ToString().ToLowerInvariant(),
                scored.Candidate.RecordedAt))];
    }
}
