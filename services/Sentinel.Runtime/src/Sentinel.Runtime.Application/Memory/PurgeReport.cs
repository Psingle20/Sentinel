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

namespace Sentinel.Runtime.Application.Memory;

/// <summary>A retention purge's report: what was (or, on a dry run, would be) deleted.</summary>
public sealed record PurgeReport(bool DryRun, IReadOnlyList<IncidentId> IncidentIds, DateTimeOffset EvaluatedAt);
