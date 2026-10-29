using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using Sentinel.Contracts.Incidents;
using Sentinel.Contracts.Messaging;
using Sentinel.Contracts.Observations;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Contracts.Remediation;
using Shouldly;

namespace Sentinel.Contracts.Tests;

public sealed partial class ContractRulesTests
{
    private static readonly Type[] MessageTypes = [.. typeof(ISentinelMessage).Assembly.GetTypes()
        .Where(type => type is { IsClass: true, IsAbstract: false } && typeof(ISentinelMessage).IsAssignableFrom(type))];

    private static string StaticValue(Type type, string name) =>
        type.GetProperty(name, BindingFlags.Public | BindingFlags.Static)!.GetValue(null)!.ToString()!;

    [GeneratedRegex(@"^(ingestion|runtime|agentworker|executor|api)\.[a-z]+(-[a-z]+)*\.[a-z]+(-[a-z]+)*$")]
    private static partial Regex EventName();

    [GeneratedRegex(@"^(ingestion|runtime|agentworker|executor|api)\.[a-z]+(-[a-z]+)+$")]
    private static partial Regex CommandName();

    [Fact]
    public void Every_message_has_a_compatibility_sample()
    {
        string[] names = [.. MessageTypes.Select(type => StaticValue(type, nameof(ISentinelMessage.MessageType)))];

        names.ShouldBeUnique();
        names.Order(StringComparer.Ordinal).ShouldBe(ContractSamples.All.Keys.Except(ContractSamples.NonMessageContracts).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Message_names_and_streams_follow_the_event_semantics()
    {
        foreach (Type type in MessageTypes)
        {
            string name = StaticValue(type, nameof(ISentinelMessage.MessageType));
            string stream = StaticValue(type, nameof(ISentinelMessage.Stream));

            if (stream.EndsWith(".commands", StringComparison.Ordinal))
            {
                CommandName().IsMatch(name).ShouldBeTrue($"{name} should be a command name like executor.execute-remediation.");
                stream.ShouldBe($"sentinel.{name.Split('.')[0]}.commands", $"{name} must go to its target service's command stream.");
            }
            else
            {
                EventName().IsMatch(name).ShouldBeTrue($"{name} should be an event name like runtime.incident.opened.");
            }

            int.Parse(StaticValue(type, nameof(ISentinelMessage.SchemaVersion)), System.Globalization.CultureInfo.InvariantCulture).ShouldBeGreaterThanOrEqualTo(1);
        }
    }

    [Fact]
    public void Required_identity_is_enforced_when_deserializing()
    {
        const string withoutTenant = """{"environmentId":"reference","incidentId":"0192f0c4-0000-7000-8000-000000000001","status":"resolved","changedAt":"2026-10-28T09:05:00+00:00","streamVersion":3}""";

        Should.Throw<JsonException>(() => JsonSerializer.Deserialize<IncidentStatusChanged>(withoutTenant, SentinelJson.Options));
    }

    [Fact]
    public void Observations_carry_their_kind_and_deserialize_to_the_right_type()
    {
        var recorded = (ObservationRecorded)ContractSamples.All[ObservationRecorded.MessageType];

        string json = JsonSerializer.Serialize(recorded, SentinelJson.Options);
        ObservationRecorded restored = JsonSerializer.Deserialize<ObservationRecorded>(json, SentinelJson.Options)!;

        json.ShouldContain("\"kind\":\"metric\"");
        restored.Observation.ShouldBeOfType<MetricObservation>().Value.ShouldBe(14.8);
        restored.TenantId.ShouldBe(TenantId.From("acme"));
    }

    [Fact]
    public void The_plan_hash_is_deterministic_and_detects_any_change_to_the_plan()
    {
        RemediationPlan draft = ContractSamples.RollbackPlan with { PlanHash = string.Empty };
        string hash = RemediationPlanHash.Compute(draft);
        RemediationPlan hashed = draft with { PlanHash = hash };

        hash.ShouldStartWith(RemediationPlanHash.Prefix);
        hash.Length.ShouldBe(RemediationPlanHash.Prefix.Length + 64);
        RemediationPlanHash.Compute(hashed).ShouldBe(hash, "the hash ignores the hash field itself");
        RemediationPlanHash.Matches(hashed).ShouldBeTrue();

        RemediationPlan tampered = hashed with
        {
            Actions = [((RollbackAction)hashed.Actions[0]) with { ToRevision = "v2.3.9" }]
        };
        RemediationPlanHash.Matches(tampered).ShouldBeFalse();
        RemediationPlanHash.Matches(hashed with { ObservationWindow = TimeSpan.FromSeconds(1) }).ShouldBeFalse();
    }

    [Fact]
    public void Identity_headers_round_trip_and_match_the_payload()
    {
        var opened = (IncidentOpened)ContractSamples.All[IncidentOpened.MessageType];

        IReadOnlyDictionary<string, string> headers = SentinelHeaders.For(opened.TenantId, opened.EnvironmentId, opened.AffectedServiceId, opened.IncidentId);

        headers[SentinelHeaders.TenantId].ShouldBe("acme");
        headers[SentinelHeaders.IncidentId].ShouldBe(opened.IncidentId.ToString());
        SentinelHeaders.MatchIdentity(headers, opened).ShouldBeTrue();
        SentinelHeaders.MatchIdentity(SentinelHeaders.For(TenantId.From("globex"), opened.EnvironmentId), opened).ShouldBeFalse();
    }
}
