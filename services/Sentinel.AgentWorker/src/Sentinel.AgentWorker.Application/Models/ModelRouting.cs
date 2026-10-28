using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Sentinel.AgentWorker.Application.Models;

/// <summary>
/// Chooses the model for an investigator role (Phase 04 model routing): Muse Spark by default once
/// configured, a more capable or self-hosted Meta model for selected roles, without domain changes.
/// </summary>
public interface IModelRouter
{
    IModelProvider For(string role);
}

/// <summary>Configured as <c>Model:Pricing</c>; estimates only, in US dollars per million tokens.</summary>
public sealed class ModelPricingOptions
{
    public const string Section = "Model:Pricing";

    public double InputPerMillionTokens { get; set; }

    public double OutputPerMillionTokens { get; set; }

    public double Estimate(ModelUsage usage)
    {
        ArgumentNullException.ThrowIfNull(usage);

        return (usage.InputTokens * InputPerMillionTokens + usage.OutputTokens * OutputPerMillionTokens) / 1_000_000;
    }
}

/// <summary>
/// AgentWorker's model and tool telemetry (Phase 04 exit criterion): latency, tokens, failures,
/// structured-output rejections and estimated cost, per provider, model and role.
/// </summary>
public sealed class AgentWorkerTelemetry : IDisposable
{
    public const string MeterName = "Sentinel.AgentWorker";

    private readonly Meter _meter = new(MeterName);

    public AgentWorkerTelemetry()
    {
        ModelDuration = _meter.CreateHistogram("sentinel.agentworker.model.duration", "s", "Time per model call.", tags: null,
            new InstrumentAdvice<double> { HistogramBucketBoundaries = [0.05, 0.1, 0.25, 0.5, 1, 2.5, 5, 10, 30, 60] });
        Tokens = _meter.CreateCounter<long>("sentinel.agentworker.model.tokens", "{token}", "Tokens sent and received, by direction.");
        ModelFailures = _meter.CreateCounter<long>("sentinel.agentworker.model.failures", "{call}", "Model calls that refused, timed out, were rate limited or failed.");
        SchemaFailures = _meter.CreateCounter<long>("sentinel.agentworker.model.schema_failures", "{output}", "Structured outputs rejected by validation.");
        EstimatedCost = _meter.CreateCounter<double>("sentinel.agentworker.model.cost", "USD", "Estimated model cost from configured token prices.");
        ToolDuration = _meter.CreateHistogram("sentinel.agentworker.tool.duration", "s", "Time per tool call.", tags: null,
            new InstrumentAdvice<double> { HistogramBucketBoundaries = [0.01, 0.025, 0.05, 0.1, 0.25, 0.5, 1, 2.5, 5] });
        ToolFailures = _meter.CreateCounter<long>("sentinel.agentworker.tool.failures", "{call}", "Tool calls that returned an error.");
    }

    public Histogram<double> ModelDuration { get; }

    public Counter<long> Tokens { get; }

    public Counter<long> ModelFailures { get; }

    public Counter<long> SchemaFailures { get; }

    public Counter<double> EstimatedCost { get; }

    public Histogram<double> ToolDuration { get; }

    public Counter<long> ToolFailures { get; }

    public void ModelCall(IModelProvider model, string role, ModelResponse response, TimeSpan duration, double cost)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(response);

        var tags = new TagList { { "gen_ai.provider.name", model.Provider }, { "gen_ai.request.model", model.Model }, { "sentinel.role", role } };
        ModelDuration.Record(duration.TotalSeconds, tags);
        Tokens.Add(response.Usage.InputTokens, [.. tags, new KeyValuePair<string, object?>("gen_ai.token.type", "input")]);
        Tokens.Add(response.Usage.OutputTokens, [.. tags, new KeyValuePair<string, object?>("gen_ai.token.type", "output")]);
        EstimatedCost.Add(cost, tags);
        if (response.Finish is not (ModelFinish.ToolCalls or ModelFinish.Final))
        {
            ModelFailures.Add(1, [.. tags, new KeyValuePair<string, object?>("sentinel.finish", response.Finish.ToString())]);
        }
    }

    public void SchemaFailure(IModelProvider model, string role)
    {
        ArgumentNullException.ThrowIfNull(model);

        SchemaFailures.Add(1, new TagList { { "gen_ai.provider.name", model.Provider }, { "gen_ai.request.model", model.Model }, { "sentinel.role", role } });
    }

    public void ToolCall(string tool, TimeSpan duration, bool failed)
    {
        ToolDuration.Record(duration.TotalSeconds, new KeyValuePair<string, object?>("sentinel.tool", tool));
        if (failed)
        {
            ToolFailures.Add(1, new KeyValuePair<string, object?>("sentinel.tool", tool));
        }
    }

    public void Dispose() => _meter.Dispose();
}
