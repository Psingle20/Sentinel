using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Sentinel.Contracts.Observations;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Contracts.Queries;
using Sentinel.Ingestion.Application.Providers;
using Sentinel.Ingestion.Domain.Observations;

namespace Sentinel.Ingestion.Infrastructure.Providers;

/// <summary>
/// Reads deployment and configuration-change events from the reference deploy controller with a
/// read-only token. Each deployment yields a <c>started</c> observation and, once it ends, a
/// <c>completed</c> or <c>failed</c> one.
/// </summary>
internal sealed class DeployControllerProvider(HttpClient http, TimeProvider time, ILogger<DeployControllerProvider> logger) : IDeploymentProvider
{
    public const string ProviderName = "deploy-controller";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public string Name => ProviderName;

    public async Task<ProviderResult<DeploymentObservation>> GetDeploymentsAsync(DeploymentRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        string uri = $"/api/deployments?since={Uri.EscapeDataString(request.Range.From.ToString("O"))}&until={Uri.EscapeDataString(request.Range.To.ToString("O"))}";
        try
        {
            List<DeploymentRecord> records = await http.GetFromJsonAsync<List<DeploymentRecord>>(new Uri(uri, UriKind.Relative), Json, cancellationToken) ?? [];
            DateTimeOffset ingestedAt = time.GetUtcNow();
            List<DeploymentObservation> observations = [.. records.SelectMany(record => Translate(record, request, ingestedAt))];
            return new ProviderResult<DeploymentObservation>(observations, request.Range.To, false, ProviderStatus.Healthy);
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException || exception is TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(exception, "Reading deployments failed");
            return ProviderResult<DeploymentObservation>.Unavailable(request.Range.From, exception.Message);
        }
    }

    private static IEnumerable<DeploymentObservation> Translate(DeploymentRecord record, DeploymentRequest request, DateTimeOffset ingestedAt)
    {
        bool completesHere = record.CompletedAt is { } completed && completed >= request.Range.From && completed < request.Range.To;
        bool startsHere = record.StartedAt >= request.Range.From && record.StartedAt < request.Range.To;
        bool instantaneous = record.CompletedAt == record.StartedAt;

        if (startsHere && !instantaneous)
        {
            yield return Observation(record, "started", record.StartedAt, "in-progress", completedAt: null, request, ingestedAt);
        }

        if (completesHere)
        {
            string state = record.Status == "failed" ? "failed" : "completed";
            yield return Observation(record, state, record.CompletedAt!.Value, state, record.CompletedAt, request, ingestedAt);
        }
    }

    private static DeploymentObservation Observation(
        DeploymentRecord record,
        string state,
        DateTimeOffset eventTime,
        string status,
        DateTimeOffset? completedAt,
        DeploymentRequest request,
        DateTimeOffset ingestedAt) => new()
        {
            ObservationId = ObservationIdentity.Deployment(ProviderName, record.DeploymentId, state),
            TenantId = request.TenantId,
            EnvironmentId = request.EnvironmentId,
            ServiceId = ServiceId.From(record.Service),
            SourceProvider = ProviderName,
            SourceResource = $"deployments/{record.DeploymentId}",
            EventTime = eventTime,
            IngestTime = ingestedAt,
            DeploymentId = record.DeploymentId,
            Version = record.Version,
            PreviousVersion = record.PreviousVersion,
            Actor = record.Actor,
            StartedAt = record.StartedAt,
            CompletedAt = completedAt,
            ConfigurationDiff = record.ConfigurationDiff,
            ChangeKind = record.Kind switch
            {
                "configChange" => "config-change",
                { } kind => kind
            },
            Status = status
        };

    /// <summary>The deploy controller's wire format; only this adapter knows it.</summary>
    private sealed record DeploymentRecord(
        string DeploymentId,
        string Service,
        string Version,
        string? PreviousVersion,
        string Kind,
        string Actor,
        string? Reason,
        string? ConfigurationDiff,
        DateTimeOffset StartedAt,
        DateTimeOffset? CompletedAt,
        string Status);
}
