using System.Text.Json;
using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Integration;

namespace Sentinel.Gateway.Application.ControlPlane;

/// <summary>A control-plane call's answer, passed through from the owning service.</summary>
public sealed record ControlPlaneResponse(int StatusCode, JsonElement? Body, string? Problem)
{
    public bool IsSuccess => StatusCode is >= 200 and < 300;
}

/// <summary>Executor's policy and operator controls (ADR-006): Api authorizes the operator, Executor owns and validates them.</summary>
public interface IExecutorControlPlane
{
    Task<ControlPlaneResponse> PolicyAsync(CancellationToken cancellationToken);

    Task<ControlPlaneResponse> PolicyHistoryAsync(CancellationToken cancellationToken);

    Task<ControlPlaneResponse> PublishPolicyAsync(JsonElement document, string expectedVersion, string author, CancellationToken cancellationToken);

    Task<ControlPlaneResponse> ControlsAsync(CancellationToken cancellationToken);

    Task<ControlPlaneResponse> SetEmergencyStopAsync(bool engaged, string actor, CancellationToken cancellationToken);

    Task<ControlPlaneResponse> SetActionDisabledAsync(string actionKind, bool disabled, string actor, CancellationToken cancellationToken);
}

/// <summary>The control-plane documents (Phase 08), each owned and validated by one service.</summary>
public static class ConfigurationAreas
{
    /// <summary>Runtime: SLO detection rules, recovery signals, the service catalog, investigators, workflow timers and consensus.</summary>
    public const string Runtime = "runtime";

    /// <summary>AgentWorker: model selection, prompt version, model budget, shadow baseline and token prices.</summary>
    public const string Models = "models";

    /// <summary>Ingestion: the metric, log and trace sources and their pull timings.</summary>
    public const string Sources = "sources";

    /// <summary>Api: tenants and environments, notification channels and routes.</summary>
    public const string Api = "api";

    public static readonly IReadOnlyList<string> All = [Runtime, Models, Sources, Api];
}

/// <summary>Reads and publishes a control-plane document at the service that owns it.</summary>
public interface IConfigurationOwners
{
    Task<ControlPlaneResponse> CurrentAsync(string area, CancellationToken cancellationToken);

    Task<ControlPlaneResponse> HistoryAsync(string area, CancellationToken cancellationToken);

    Task<ControlPlaneResponse> PublishAsync(string area, JsonElement document, string expectedVersion, string author, CancellationToken cancellationToken);
}

/// <summary>Ingestion's provider sources with health and watermarks, for administrators.</summary>
public interface ISourceHealth
{
    Task<ControlPlaneResponse> SourcesAsync(CancellationToken cancellationToken);
}

public sealed record AuditEntry(long Sequence, string Actor, string Action, string Target, JsonElement Detail, DateTimeOffset OccurredAt);

/// <summary>Api's audit history of control-plane changes (Phase 08): who changed what, when, with what result.</summary>
public interface IAuditLog
{
    Task RecordAsync(string actor, string action, string target, object detail, DateTimeOffset occurredAt, CancellationToken cancellationToken);

    Task<IReadOnlyList<AuditEntry>> RecentAsync(int limit, CancellationToken cancellationToken);
}

/// <summary>
/// Control-plane changes (Phase 08): every change goes to the service that owns the setting, which
/// validates it, and is audited here whether it succeeded or was refused.
/// </summary>
public sealed class ControlPlaneService(IExecutorControlPlane executor, IConfigurationOwners owners, IAuditLog audit, IUnitOfWork unitOfWork, TimeProvider time)
{
    private static readonly ControlPlaneResponse UnknownArea = new(404, null, $"The configuration areas are {string.Join(", ", ConfigurationAreas.All)}.");

    public Task<ControlPlaneResponse> ConfigurationAsync(string area, CancellationToken cancellationToken) =>
        ConfigurationAreas.All.Contains(area) ? owners.CurrentAsync(area, cancellationToken) : Task.FromResult(UnknownArea);

    public Task<ControlPlaneResponse> ConfigurationHistoryAsync(string area, CancellationToken cancellationToken) =>
        ConfigurationAreas.All.Contains(area) ? owners.HistoryAsync(area, cancellationToken) : Task.FromResult(UnknownArea);

    public async Task<ControlPlaneResponse> PublishConfigurationAsync(string area, JsonElement document, string expectedVersion, string actor, CancellationToken cancellationToken)
    {
        if (!ConfigurationAreas.All.Contains(area))
        {
            return UnknownArea;
        }

        ControlPlaneResponse response = await owners.PublishAsync(area, document, expectedVersion, actor, cancellationToken);
        string version = document.ValueKind == JsonValueKind.Object && document.TryGetProperty("version", out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString()! : "?";
        await RecordAsync(actor, "configuration.publish", $"{area}/{version}", new { expectedVersion, response.StatusCode, response.Problem }, cancellationToken);
        return response;
    }
    public async Task<ControlPlaneResponse> PublishPolicyAsync(JsonElement document, string expectedVersion, string actor, CancellationToken cancellationToken)
    {
        ControlPlaneResponse response = await executor.PublishPolicyAsync(document, expectedVersion, actor, cancellationToken);
        string version = document.TryGetProperty("version", out JsonElement value) ? value.GetString() ?? "?" : "?";
        await RecordAsync(actor, "policy.publish", $"policy/{version}", new { expectedVersion, response.StatusCode, response.Problem }, cancellationToken);
        return response;
    }

    public async Task<ControlPlaneResponse> SetEmergencyStopAsync(bool engaged, string actor, CancellationToken cancellationToken)
    {
        ControlPlaneResponse response = await executor.SetEmergencyStopAsync(engaged, actor, cancellationToken);
        await RecordAsync(actor, engaged ? "controls.emergency-stop.engage" : "controls.emergency-stop.release", "executor/emergency-stop", new { response.StatusCode, response.Problem }, cancellationToken);
        return response;
    }

    public async Task<ControlPlaneResponse> SetActionDisabledAsync(string actionKind, bool disabled, string actor, CancellationToken cancellationToken)
    {
        ControlPlaneResponse response = await executor.SetActionDisabledAsync(actionKind, disabled, actor, cancellationToken);
        await RecordAsync(actor, disabled ? "controls.action.disable" : "controls.action.enable", $"executor/actions/{actionKind}", new { response.StatusCode, response.Problem }, cancellationToken);
        return response;
    }

    private async Task RecordAsync(string actor, string action, string target, object detail, CancellationToken cancellationToken)
    {
        await audit.RecordAsync(actor, action, target, detail, time.GetUtcNow(), cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
    }
}
