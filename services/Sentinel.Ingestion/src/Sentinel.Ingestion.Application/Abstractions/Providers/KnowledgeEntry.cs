using CleanArchitecture.BuildingBlocks;
using Microsoft.Extensions.Options;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Contracts.Queries;
using Sentinel.Ingestion.Domain.Observations;

namespace Sentinel.Ingestion.Application.Abstractions.Providers;

/// <summary>A knowledge document as a provider returns it, before redaction and ranking.</summary>
public sealed record KnowledgeEntry(string DocumentId, string Title, string? Service, string Body, string Source, DateTimeOffset UpdatedAt);
