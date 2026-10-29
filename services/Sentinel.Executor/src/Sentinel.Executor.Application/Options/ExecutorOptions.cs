using System.Globalization;
using System.Text.Json;
using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Integration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sentinel.Contracts.Messaging;
using Sentinel.Contracts.Remediation;
using Sentinel.Executor.Domain.Capabilities;
using Sentinel.Executor.Domain.Executions;
using Sentinel.Executor.Domain.Policy;
using DomainVerdict = Sentinel.Executor.Domain.Policy.PolicyVerdict;

namespace Sentinel.Executor.Application.Options;

/// <summary>Configured as <c>Executor</c>.</summary>
public sealed class ExecutorOptions
{
    public const string Section = "Executor";

    /// <summary>Denies every action, independent of Runtime, Api and every model path (ADR-006). Operators can also engage it at run time.</summary>
    public bool EmergencyStop { get; set; }

    /// <summary>The remediation policy document, relative to the content root.</summary>
    public string PolicyFile { get; set; } = "policies/remediation-policy.json";

    /// <summary>How long action verification waits for the target to show the action took effect.</summary>
    public TimeSpan VerifyTimeout { get; set; } = TimeSpan.FromSeconds(90);

    public TimeSpan VerifyPollInterval { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>An unfinished request untouched for this long is reconciled.</summary>
    public TimeSpan ReconcileAfter { get; set; } = TimeSpan.FromSeconds(20);

    /// <summary>A capability expires this long after it is issued; the adapter call must start before then.</summary>
    public TimeSpan CapabilityLifetime { get; set; } = TimeSpan.FromMinutes(2);
}
