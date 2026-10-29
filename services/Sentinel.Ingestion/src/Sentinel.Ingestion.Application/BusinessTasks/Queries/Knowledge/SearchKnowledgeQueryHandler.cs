using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Messaging;
using Microsoft.Extensions.Options;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Contracts.Queries;
using Sentinel.Ingestion.Application.Abstractions.Providers;
using Sentinel.Ingestion.Application.Options;
using Sentinel.Ingestion.Domain.Observations;

namespace Sentinel.Ingestion.Application.BusinessTasks.Queries.Knowledge;

/// <summary>
/// Keyword search over knowledge documents, inside the query bulkhead. Passages are redacted with the
/// same policy as telemetry and returned as data: a runbook that contains instructions is still only text.
/// </summary>
public sealed class SearchKnowledgeQueryHandler(
    IEnumerable<IKnowledgeProvider> providers,
    QueryBulkhead bulkhead,
    IOptions<IngestionOptions> options) : IQueryHandler<SearchKnowledgeQuery, KnowledgeResult>
{
    public const int MaxExcerptLength = 1200;

    public Task<Result<KnowledgeResult>> Handle(SearchKnowledgeQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        return bulkhead.RunAsync(token => SearchAsync(query.Query, token), cancellationToken);
    }

    private async Task<Result<KnowledgeResult>> SearchAsync(KnowledgeQuery query, CancellationToken cancellationToken)
    {
        if (query.TenantId.Value != options.Value.Tenant || query.EnvironmentId.Value != options.Value.Environment)
        {
            return Result.Failure<KnowledgeResult>(QueryErrors.UnknownScope);
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
