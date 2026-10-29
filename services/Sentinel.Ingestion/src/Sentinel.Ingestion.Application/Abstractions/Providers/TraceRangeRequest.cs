using Sentinel.Contracts;
using Sentinel.Contracts.Observations;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Ingestion.Application.Options;

namespace Sentinel.Ingestion.Application.Abstractions.Providers;

public sealed record TraceRangeRequest(TenantId TenantId, EnvironmentId EnvironmentId, TraceSourceDefinition Source, TimeRange Range);
