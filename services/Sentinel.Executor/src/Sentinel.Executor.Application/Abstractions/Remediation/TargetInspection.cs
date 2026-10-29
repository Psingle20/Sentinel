using System.Security.Cryptography;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Contracts.Remediation;
using Sentinel.Executor.Domain.Capabilities;
using Sentinel.Executor.Domain.Executions;
using Sentinel.Executor.Domain.Policy;

namespace Sentinel.Executor.Application.Abstractions.Remediation;

/// <summary>The target's actual state: what is serving, and what became of this request's operation.</summary>
public sealed record TargetInspection(string? ServingRevision, OperationStatus Operation, string Detail);
