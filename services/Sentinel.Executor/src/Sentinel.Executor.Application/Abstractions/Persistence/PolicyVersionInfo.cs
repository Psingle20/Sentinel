using System.Security.Cryptography;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Contracts.Remediation;
using Sentinel.Executor.Domain.Capabilities;
using Sentinel.Executor.Domain.Executions;
using Sentinel.Executor.Domain.Policy;

namespace Sentinel.Executor.Application.Abstractions.Persistence;

/// <summary>A published policy version, for the control plane's history.</summary>
public sealed record PolicyVersionInfo(string Version, string Author, DateTimeOffset PublishedAt, string Source);
