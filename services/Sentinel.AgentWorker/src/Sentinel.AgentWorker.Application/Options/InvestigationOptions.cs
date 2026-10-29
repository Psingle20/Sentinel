using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using Sentinel.Contracts;
using Sentinel.Contracts.Messaging;
using Sentinel.Contracts.Observations;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Contracts.Queries;

namespace Sentinel.AgentWorker.Application.Options;

/// <summary>Configured as <c>Investigation</c>.</summary>
public sealed class InvestigationOptions
{
    public const string Section = "Investigation";

    /// <summary><c>model</c> (the generalist investigator) or <c>deploy-blame</c> (the baseline).</summary>
    public string Investigator { get; set; } = "model";

    /// <summary>Runs the deploy-blame baseline in shadow on every investigation, for comparison.</summary>
    public bool ShadowBaseline { get; set; } = true;

    /// <summary>The investigator prompt version (<see cref="Investigations.InvestigatorPrompt.Versions"/>); every execution records it.</summary>
    public string PromptVersion { get; set; } = Investigations.InvestigatorPrompt.Version;

    public int MaxModelTurns { get; set; } = 6;

    public int MaxOutputTokens { get; set; } = 4_000;

    /// <summary>The metric names tools may query, with what each measures; shown to the model.</summary>
    public IDictionary<string, string> Metrics { get; } = new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>Most points per series returned to the model; longer series are down-sampled.</summary>
    public int MaxPointsPerSeries { get; set; } = 40;
}
