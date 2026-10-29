using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Configuration;
using Sentinel.Ingestion.Application.Providers;

namespace Sentinel.Ingestion.Application;

/// <summary>
/// Ingestion's control-plane document (Phase 08 provider configuration): which metric series, log
/// streams and trace sources are pulled, how often, and how far back. Provider endpoints and
/// credentials stay deployment configuration and are never part of it (ADR-007), and so do the
/// tenant, the environment and the query bulkhead. A published version restarts the pull loops
/// with the new sources once their current pulls finish.
/// </summary>
public sealed record SourceConfigurationDocument : IVersionedDocument
{
    public required string Version { get; init; }

    public TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(5);

    public TimeSpan MetricStep { get; init; } = TimeSpan.FromSeconds(5);

    public TimeSpan InitialLookback { get; init; } = TimeSpan.FromMinutes(2);

    public TimeSpan MaxRangePerPull { get; init; } = TimeSpan.FromMinutes(5);

    public TimeSpan LogLag { get; init; } = TimeSpan.FromSeconds(30);

    public TimeSpan TraceLag { get; init; } = TimeSpan.FromSeconds(30);

    public TimeSpan LateDataOverlap { get; init; } = TimeSpan.FromSeconds(30);

    public bool TopologyEnabled { get; init; }

    public TimeSpan TopologyInterval { get; init; } = TimeSpan.FromMinutes(1);

    public TimeSpan TopologyLookback { get; init; } = TimeSpan.FromMinutes(15);

    public IReadOnlyList<MetricSeriesDefinition> MetricSeries { get; init; } = [];

    public IReadOnlyList<LogSourceDefinition> LogSources { get; init; } = [];

    public IReadOnlyList<TraceSourceDefinition> TraceSources { get; init; } = [];
}

/// <summary>Moves Ingestion's options to and from the control-plane document.</summary>
public static class SourceConfigurationMapping
{
    public const string DeploymentVersion = "deployment";

    public static SourceConfigurationDocument From(IngestionOptions options, string version = DeploymentVersion)
    {
        ArgumentNullException.ThrowIfNull(options);

        return new SourceConfigurationDocument
        {
            Version = version,
            PollInterval = options.PollInterval,
            MetricStep = options.MetricStep,
            InitialLookback = options.InitialLookback,
            MaxRangePerPull = options.MaxRangePerPull,
            LogLag = options.LogLag,
            TraceLag = options.TraceLag,
            LateDataOverlap = options.LateDataOverlap,
            TopologyEnabled = options.TopologyEnabled,
            TopologyInterval = options.TopologyInterval,
            TopologyLookback = options.TopologyLookback,
            MetricSeries = [.. options.MetricSeries],
            LogSources = [.. options.LogSources],
            TraceSources = [.. options.TraceSources]
        };
    }

    public static void Apply(IngestionOptions options, SourceConfigurationDocument document)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(document);

        options.PollInterval = document.PollInterval;
        options.MetricStep = document.MetricStep;
        options.InitialLookback = document.InitialLookback;
        options.MaxRangePerPull = document.MaxRangePerPull;
        options.LogLag = document.LogLag;
        options.TraceLag = document.TraceLag;
        options.LateDataOverlap = document.LateDataOverlap;
        options.TopologyEnabled = document.TopologyEnabled;
        options.TopologyInterval = document.TopologyInterval;
        options.TopologyLookback = document.TopologyLookback;
        Replace(options.MetricSeries, document.MetricSeries);
        Replace(options.LogSources, document.LogSources);
        Replace(options.TraceSources, document.TraceSources);
    }

    /// <summary>Why the document cannot run; empty when it can.</summary>
    public static IReadOnlyList<string> Problems(SourceConfigurationDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var problems = new List<string>();
        if (document.PollInterval < TimeSpan.FromSeconds(1) || document.PollInterval > TimeSpan.FromMinutes(5))
        {
            problems.Add("The poll interval is between 1 second and 5 minutes.");
        }

        if (document.MetricStep <= TimeSpan.Zero || document.MaxRangePerPull <= document.MetricStep || document.InitialLookback <= TimeSpan.Zero)
        {
            problems.Add("The metric step is positive, a pull covers more than one step, and the initial lookback is positive.");
        }

        if (document.LogLag < TimeSpan.Zero || document.TraceLag < TimeSpan.Zero || document.LateDataOverlap < TimeSpan.Zero || document.TopologyInterval <= TimeSpan.Zero || document.TopologyLookback <= TimeSpan.Zero)
        {
            problems.Add("Lags and the late-data overlap are not negative, and the topology interval and lookback are positive.");
        }

        if (document.MetricSeries.Count == 0)
        {
            problems.Add("At least one metric series is required: detection depends on it.");
        }

        IEnumerable<string> names = document.MetricSeries.Select(series => $"metrics:{series.Name}")
            .Concat(document.LogSources.Select(source => $"logs:{source.Name}"))
            .Concat(document.TraceSources.Select(source => $"traces:{source.Name}"));
        if (names.GroupBy(name => name, StringComparer.Ordinal).FirstOrDefault(group => group.Count() > 1) is { } duplicate)
        {
            problems.Add($"Source '{duplicate.Key}' is defined more than once.");
        }

        foreach (MetricSeriesDefinition series in document.MetricSeries)
        {
            if (string.IsNullOrWhiteSpace(series.Name) || string.IsNullOrWhiteSpace(series.Expression) || series.Expression.Length > 2_000)
            {
                problems.Add("Every metric series has a name and an expression of at most 2,000 characters.");
            }

            if (series.Service is null == series.ServiceLabel is null)
            {
                problems.Add($"Metric series '{series.Name}' names either a fixed service or a service label, not both or neither.");
            }

            if (series.Priority is < -10 or > 10)
            {
                problems.Add($"Metric series '{series.Name}' has a priority between -10 and 10.");
            }
        }

        foreach (LogSourceDefinition source in document.LogSources)
        {
            if (string.IsNullOrWhiteSpace(source.Name) || string.IsNullOrWhiteSpace(source.Query) || source.Query.Length > 2_000 || source.Limit is < 1 or > 5_000)
            {
                problems.Add($"Log source '{source.Name}' has a name, a query of at most 2,000 characters and a limit of 1 to 5,000 lines.");
            }
        }

        foreach (TraceSourceDefinition source in document.TraceSources)
        {
            if (string.IsNullOrWhiteSpace(source.Name) || source.Services.Count == 0 || source.SlowTraceLimit is < 0 or > 500 || source.SampleTraceLimit is < 0 or > 100)
            {
                problems.Add($"Trace source '{source.Name}' names its services and keeps its trace limits within 500 slow and 100 sampled traces.");
            }
        }

        return problems;
    }

    private static void Replace<T>(IList<T> target, IEnumerable<T> items)
    {
        target.Clear();
        foreach (T item in items)
        {
            target.Add(item);
        }
    }
}

/// <summary>Reads and publishes Ingestion's control-plane document; the deployment configuration seeds it.</summary>
public sealed class SourceConfigurationService(VersionedDocumentPublisher<SourceConfigurationDocument> publisher, Func<IngestionOptions> deployment)
{
    public Task<DocumentState<SourceConfigurationDocument>> CurrentAsync(CancellationToken cancellationToken) =>
        publisher.CurrentAsync(Seed, cancellationToken);

    public Task<IReadOnlyList<DocumentVersion>> HistoryAsync(CancellationToken cancellationToken) =>
        publisher.HistoryAsync(Seed, 100, cancellationToken);

    public Task<Result<PublishedDocument<SourceConfigurationDocument>>> PublishAsync(SourceConfigurationDocument document, string expectedVersion, string author, CancellationToken cancellationToken) =>
        publisher.PublishAsync(document, expectedVersion, author, Seed, Validate, cancellationToken);

    private SourceConfigurationDocument Seed() => SourceConfigurationMapping.From(deployment());

    private IReadOnlyList<string> Validate(SourceConfigurationDocument document)
    {
        IReadOnlyList<string> problems = SourceConfigurationMapping.Problems(document);
        if (problems.Count > 0)
        {
            return problems;
        }

        IngestionOptions candidate = deployment();
        SourceConfigurationMapping.Apply(candidate, document);
        return candidate.IsValid() ? [] : ["The configuration does not satisfy Ingestion's startup rules."];
    }
}
