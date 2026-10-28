using CleanArchitecture.BuildingBlocks;
using Microsoft.Extensions.Options;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Contracts.Queries;
using Sentinel.Ingestion.Domain.Observations;

namespace Sentinel.Ingestion.Application.Knowledge;

/// <summary>A knowledge document as a provider returns it, before redaction and ranking.</summary>
public sealed record KnowledgeEntry(string DocumentId, string Title, string? Service, string Body, string Source, DateTimeOffset UpdatedAt);

/// <summary>A knowledge provider, such as a runbook repository (ADR-014). Owned by Ingestion like every provider.</summary>
public interface IKnowledgeProvider
{
    string Name { get; }

    Task<IReadOnlyList<KnowledgeEntry>> LoadAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Keyword search over knowledge documents. Passages are redacted with the same policy as
/// telemetry and returned as data: a runbook that contains instructions is still only text.
/// </summary>
public sealed class KnowledgeSearch(IEnumerable<IKnowledgeProvider> providers, IOptions<IngestionOptions> options)
{
    public const int MaxExcerptLength = 1200;

    public async Task<Result<KnowledgeResult>> SearchAsync(KnowledgeQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (query.TenantId.Value != options.Value.Tenant || query.EnvironmentId.Value != options.Value.Environment)
        {
            return Result.Failure<KnowledgeResult>(ObservationQueryService.UnknownScope);
        }

        string[] terms = [.. query.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(term => term.ToLowerInvariant())
            .Where(term => term.Length > 2)
            .Distinct()];
        if (terms.Length == 0)
        {
            return Result.Success(new KnowledgeResult([], RedactionPolicy.Version));
        }

        var matches = new List<KnowledgeDocument>();
        foreach (IKnowledgeProvider provider in providers)
        {
            foreach (KnowledgeEntry entry in await provider.LoadAsync(cancellationToken))
            {
                if (query.ServiceId is { } service && entry.Service is not null && entry.Service != service.Value)
                {
                    continue;
                }

                string[] paragraphs = entry.Body.Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                (string Paragraph, int Hits) best = paragraphs
                    .Select(paragraph => (Paragraph: paragraph, Hits: terms.Count(term => paragraph.Contains(term, StringComparison.OrdinalIgnoreCase))))
                    .OrderByDescending(candidate => candidate.Hits)
                    .FirstOrDefault();
                int titleHits = terms.Count(term => entry.Title.Contains(term, StringComparison.OrdinalIgnoreCase));
                if (best.Hits + titleHits == 0)
                {
                    continue;
                }

                string excerpt = RedactionPolicy.Apply(best.Paragraph).Text;
                matches.Add(new KnowledgeDocument
                {
                    DocumentId = $"{provider.Name}:{entry.DocumentId}",
                    Title = entry.Title,
                    ServiceId = entry.Service is { } owner ? ServiceId.From(owner) : null,
                    Excerpt = excerpt.Length > MaxExcerptLength ? excerpt[..MaxExcerptLength] : excerpt,
                    Source = entry.Source,
                    UpdatedAt = entry.UpdatedAt,
                    Score = (double)(best.Hits + 2 * titleHits) / (terms.Length * 3)
                });
            }
        }

        return Result.Success(new KnowledgeResult([.. matches.OrderByDescending(match => match.Score).Take(Math.Clamp(query.Limit, 1, 20))], RedactionPolicy.Version));
    }
}
