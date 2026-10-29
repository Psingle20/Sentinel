using CleanArchitecture.BuildingBlocks;
using Sentinel.Contracts.Incidents;
using Sentinel.Contracts.Observations;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Contracts.Queries;
using Sentinel.Contracts.Remediation;
using Sentinel.Runtime.Application.Incidents;
using Sentinel.Runtime.Domain.Correlation;
using Sentinel.Runtime.Domain.Incidents;

namespace Sentinel.Runtime.Application.Abstractions.Runtime;

/// <summary>Hot context for one service (ServiceGrain): catalog facts and observed dependencies. A cache, never authoritative.</summary>
public sealed record ServiceContext(ServiceId ServiceId, string? Owner, IReadOnlyList<ServiceId> Dependencies, IReadOnlyList<ServiceId> Dependents);
