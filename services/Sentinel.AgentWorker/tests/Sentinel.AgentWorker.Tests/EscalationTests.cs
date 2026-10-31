using System.Globalization;
using System.Text.Json.Nodes;
using Sentinel.AgentWorker.Application.Abstractions.Providers;
using Sentinel.AgentWorker.Application.Investigations;
using Sentinel.Contracts;
using Sentinel.Contracts.Investigations;
using Sentinel.Contracts.Primitives.Identifiers;
using Shouldly;

namespace Sentinel.AgentWorker.Tests;

/// <summary>Phase E05: the escalation investigator proposes one discriminating query; AgentWorker validates, runs and measures it.</summary>
public sealed class EscalationTests
{
    private static readonly ServiceId Checkout = ServiceId.From("checkout-service");
    private static readonly ServiceId Orders = ServiceId.From("order-service");

    private static readonly Dictionary<string, string> Metrics = new(Harness.Catalog.Value.Metrics, StringComparer.Ordinal);

    [Fact]
    public async Task In_the_ambiguous_connection_growth_the_query_supports_the_leak_and_contradicts_the_pool()
    {
        var telemetry = Telemetry.AmbiguousConnectionGrowth();
        (InvestigateIncident command, HypothesisFinding leak, HypothesisFinding pool) = Ambiguous(telemetry);

        InvestigationRun run = await Harness.Escalation(telemetry).InvestigateAsync(command, false, CancellationToken.None);

        run.Completed.Role.ShouldBe(InvestigationRoles.Escalation);
        run.Completed.Outcome.ShouldBe(InvestigationOutcome.Completed);
        DiscriminatingQuery query = run.Completed.Query.ShouldNotBeNull();
        query.Tool.ShouldBe("metric_range");
        query.Expectations.Select(expectation => expectation.ServiceId).ShouldBe([Checkout, Orders]);
        run.Completed.Hypotheses.ShouldBeEmpty();
        run.Completed.Evidence.Single(evidence => evidence.HypothesisIds.Contains(leak.HypothesisId)).Direction.ShouldBe(EvidenceDirection.Supports);
        run.Completed.Evidence.Single(evidence => evidence.HypothesisIds.Contains(pool.HypothesisId)).Direction.ShouldBe(EvidenceDirection.Contradicts);
        run.Completed.Evidence.ShouldAllBe(evidence => evidence.IndependenceGroup == "escalation:redis_connections" && evidence.ToolCallId == "call_escalation_query");
        run.Execution.ToolCalls.Single().ToolName.ShouldBe("metric_range");
        run.Completed.Evidence.SelectMany(evidence => evidence.SourceObservationIds).ShouldAllBe(id => run.Execution.ToolCalls.Single().ObservationIds.Contains(id));
    }

    [Theory]
    [InlineData("tool", "\"slow_spans\"", "is not available")]
    [InlineData("diagnosis", "\"the leak\"", "'diagnosis' is not part of a discriminating query")]
    [InlineData("actions", "[]", "'actions' is not part of a discriminating query")]
    public void A_proposal_with_another_tool_or_a_conclusion_is_refused(string field, string json, string reason)
    {
        var telemetry = Telemetry.AmbiguousConnectionGrowth();
        (InvestigateIncident command, HypothesisFinding leak, HypothesisFinding pool) = Ambiguous(telemetry);
        JsonObject proposal = Proposal(leak, pool);
        proposal[field] = JsonNode.Parse(json);

        (DiscriminatingQuery? query, IReadOnlyList<string> errors) = DiscriminatingQueries.Parse(proposal.ToJsonString(), command, Metrics);

        query.ShouldBeNull();
        errors.ShouldContain(error => error.Contains(reason, StringComparison.Ordinal));
    }

    [Fact]
    public void A_range_outside_the_window_an_unknown_metric_or_a_stranger_hypothesis_is_refused()
    {
        var telemetry = Telemetry.AmbiguousConnectionGrowth();
        (InvestigateIncident command, HypothesisFinding leak, HypothesisFinding pool) = Ambiguous(telemetry);
        JsonObject widened = Proposal(leak, pool);
        widened["arguments"]!["from"] = command.ObservationWindow.From.AddHours(-1).ToString("O", CultureInfo.InvariantCulture);
        JsonObject unknown = Proposal(leak, pool, metric: "cpu_seconds");
        JsonObject stranger = Proposal(leak, pool);
        stranger["hypothesisIds"]![1] = HypothesisId.New().ToString();

        Refused(widened, command).ShouldContain(error => error.Contains("inside the incident's observation window", StringComparison.Ordinal));
        Refused(unknown, command).ShouldContain(error => error.Contains("is not in the catalogue", StringComparison.Ordinal));
        Refused(stranger, command).ShouldContain(error => error.Contains("must name the two explanations", StringComparison.Ordinal));
        DiscriminatingQueries.Parse(Proposal(leak, pool).ToJsonString(), command, Metrics).Query.ShouldNotBeNull();
    }

    [Fact]
    public void When_both_predictions_hold_the_query_separates_nothing_and_says_so()
    {
        var telemetry = Telemetry.AmbiguousConnectionGrowth();
        (InvestigateIncident command, HypothesisFinding leak, HypothesisFinding pool) = Ambiguous(telemetry);
        DiscriminatingQuery query = DiscriminatingQueries.Parse(Proposal(leak, pool, threshold: 10).ToJsonString(), command, Metrics).Query.ShouldNotBeNull();

        (IReadOnlyList<EvidenceFinding> evidence, string? unknown) = DiscriminatingQueries.Measure(query, Series(("checkout-service", 150), ("order-service", 40)), "call_1", Telemetry.Start.AddSeconds(60));

        evidence.Single().Direction.ShouldBe(EvidenceDirection.Neutral);
        evidence.Single().HypothesisIds.ShouldBe([leak.HypothesisId, pool.HypothesisId]);
        unknown.ShouldNotBeNull();
    }

    [Fact]
    public void Each_prediction_is_read_at_onset_not_at_the_end_of_the_window()
    {
        var telemetry = Telemetry.AmbiguousConnectionGrowth();
        (InvestigateIncident command, HypothesisFinding leak, HypothesisFinding pool) = Ambiguous(telemetry);
        DiscriminatingQuery query = DiscriminatingQueries.Parse(Proposal(leak, pool).ToJsonString(), command, Metrics).Query.ShouldNotBeNull();
        string content = new JsonObject
        {
            ["series"] = new JsonArray(
                Service("checkout-service", (55, 150), (120, 4)),
                Service("order-service", (55, 40), (120, 160)))
        }.ToJsonString();

        (IReadOnlyList<EvidenceFinding> evidence, _) = DiscriminatingQueries.Measure(query, content, "call_1", Telemetry.Start.AddSeconds(60));

        evidence.Single(item => item.HypothesisIds.Contains(leak.HypothesisId)).Direction.ShouldBe(EvidenceDirection.Supports);
        evidence.Single(item => item.HypothesisIds.Contains(pool.HypothesisId)).Direction.ShouldBe(EvidenceDirection.Contradicts);
    }

    [Fact]
    public async Task A_refused_proposal_is_repaired_once_and_then_the_investigator_abstains()
    {
        var telemetry = Telemetry.AmbiguousConnectionGrowth();
        (InvestigateIncident command, _, _) = Ambiguous(telemetry);
        var model = new AnswerModel("""{"diagnosis":"checkout-service","tool":"metric_range"}""");

        InvestigationRun run = await Harness.Escalation(telemetry, model).InvestigateAsync(command, false, CancellationToken.None);

        model.Calls.ShouldBe(2);
        run.Completed.Outcome.ShouldBe(InvestigationOutcome.Abstained);
        run.Completed.Evidence.ShouldBeEmpty();
        run.Execution.ToolCalls.ShouldBeEmpty();
        telemetry.Queries.ShouldBe(0);
    }

    /// <summary>
    /// S11 as the specialists left it: the leak and the order pool each rest on a connection surge of their own service and
    /// a deployment that served before onset, and consensus could not choose.
    /// </summary>
    internal static (InvestigateIncident Command, HypothesisFinding Leak, HypothesisFinding Pool) Ambiguous(Telemetry telemetry)
    {
        HypothesisFinding leak = Change("d1", Checkout);
        HypothesisFinding pool = Change("d16", Orders);
        InvestigateIncident command = telemetry.Command(70, InvestigationRoles.Escalation) with
        {
            Hypotheses = [leak, pool],
            Evidence = [Surge(leak, Checkout), Surge(pool, Orders)],
            Escalation = new EscalationContext
            {
                Standing = [new HypothesisStanding(leak.HypothesisId, 1.1, 2, 0), new HypothesisStanding(pool.HypothesisId, 1.0, 2, 0)],
                EvidenceCompleteness = 1,
                Rationale = "Confidence 0.42; the runner-up scored close to the leader."
            }
        };
        return (command, leak, pool);
    }

    private static JsonObject Proposal(HypothesisFinding leak, HypothesisFinding pool, string metric = "redis_connections", double threshold = 100) => new()
    {
        ["hypothesisIds"] = new JsonArray(leak.HypothesisId.ToString(), pool.HypothesisId.ToString()),
        ["tool"] = "metric_range",
        ["arguments"] = new JsonObject { ["metric"] = metric },
        ["expectations"] = new JsonArray(
            Expectation(leak, "checkout-service", metric, threshold),
            Expectation(pool, "order-service", metric, threshold)),
        ["rationale"] = "Only the cause holds most of Redis's clients."
    };

    private static JsonObject Expectation(HypothesisFinding hypothesis, string service, string metric, double threshold) => new()
    {
        ["hypothesisId"] = hypothesis.HypothesisId.ToString(),
        ["service"] = service,
        ["metric"] = metric,
        ["comparison"] = "above",
        ["threshold"] = threshold,
        ["statement"] = $"{service} holds more than {threshold} connections at onset."
    };

    private static IReadOnlyList<string> Refused(JsonObject proposal, InvestigateIncident command)
    {
        (DiscriminatingQuery? query, IReadOnlyList<string> errors) = DiscriminatingQueries.Parse(proposal.ToJsonString(), command, Metrics);
        query.ShouldBeNull();
        return errors;
    }

    private static string Series(params (string Service, double Value)[] series) =>
        new JsonObject { ["series"] = new JsonArray([.. series.Select(item => (JsonNode)Service(item.Service, (55, item.Value)))]) }.ToJsonString();

    private static JsonObject Service(string service, params (int Second, double Value)[] samples) => new()
    {
        ["service"] = service,
        ["samples"] = new JsonArray([.. samples.Select(sample => (JsonNode)new JsonObject
        {
            ["id"] = $"prometheus:redis_connections:{service}:{sample.Second + 1000}",
            ["t"] = Telemetry.Start.AddSeconds(sample.Second).ToString("O", CultureInfo.InvariantCulture),
            ["v"] = sample.Value
        })])
    };

    private static EvidenceFinding Surge(HypothesisFinding hypothesis, ServiceId service) => new()
    {
        EvidenceId = EvidenceId.New(),
        Statement = $"{service} Redis connections rose after its deployment served.",
        Direction = EvidenceDirection.Supports,
        Strength = 0.8,
        SourceObservationIds = [ObservationId.From($"prometheus:redis_connections:{service}:1040")],
        HypothesisIds = [hypothesis.HypothesisId],
        EventTimeRange = new TimeRange(Telemetry.Start.AddSeconds(20), Telemetry.Start.AddSeconds(60)),
        ToolCallId = "call_1",
        IndependenceGroup = $"redis-connections:{service}"
    };

    private static HypothesisFinding Change(string deployment, ServiceId service)
    {
        var change = ObservationId.From($"deploy-controller:deployment:{deployment}:started");
        return new HypothesisFinding
        {
            HypothesisId = HypothesisId.New(),
            Key = HypothesisKeys.Change(change),
            Statement = $"The {deployment} deployment caused the symptoms.",
            CausalChain = ["deploy", "Redis connections", "SLO burn"],
            SuspectedServiceId = service,
            SuspectedChangeObservationId = change
        };
    }

    /// <summary>A model that gives the same final answer every time and counts how often it was asked.</summary>
    private sealed class AnswerModel(string answer) : IModelProvider
    {
        public int Calls { get; private set; }

        public string Provider => "scripted";

        public string Model => "scripted";

        public string? ModelVersion => null;

        public Task<ModelResponse> CompleteAsync(ModelRequest request, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new ModelResponse(ModelFinish.Final, [], answer, new ModelUsage(10, 10)));
        }
    }
}
