using Sentinel.Contracts;
using Sentinel.Contracts.Observations;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Ingestion.Application.Options;
using Sentinel.Contracts.Queries;

namespace Sentinel.Ingestion.Application.Abstractions.Providers;

public sealed record MetricRangeRequest(TenantId TenantId, EnvironmentId EnvironmentId, MetricSeriesDefinition Series, TimeRange Range, TimeSpan Step);
