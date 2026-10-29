using CleanArchitecture.BuildingBlocks;
using Sentinel.Contracts;
using Sentinel.Contracts.Incidents;
using Sentinel.Contracts.Observations;
using Sentinel.Contracts.Queries;
using Sentinel.Contracts.Remediation;
using Sentinel.Runtime.Application.Incidents;
using Sentinel.Runtime.Domain.Incidents;
using Sentinel.Runtime.Application.Abstractions.Runtime;

namespace Sentinel.Runtime.Application.Recovery;

/// <summary>What recovery verification found; untrustworthy answers are retried until the deadline.</summary>
public sealed record RecoveryAssessment(bool Trustworthy, RecoveryResult Result, IReadOnlyList<Measurement> Measurements, DateTimeOffset ObservedThrough, string? Reason);
