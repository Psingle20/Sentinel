using Sentinel.AgentWorker.Application.Investigations;
using Sentinel.Contracts.Primitives.Identifiers;
using Shouldly;

namespace Sentinel.AgentWorker.Tests;

public sealed class FindingsValidatorTests
{
    private static readonly HashSet<ObservationId> Returned = [ObservationId.From("obs:1"), ObservationId.From("obs:2")];
    private static readonly HashSet<string> Calls = new(StringComparer.Ordinal) { "call_1" };

    private const string Valid = """
        {
          "evidence": [{ "id": "e1", "statement": "Connections rose.", "direction": "supports", "strength": 0.8,
                         "observationIds": ["obs:1", "obs:2"], "hypothesisIds": ["h1"],
                         "from": "2026-10-28T09:00:00Z", "to": "2026-10-28T09:01:00Z", "toolCallId": "call_1" }],
          "hypotheses": [
            { "id": "h1", "statement": "A leak.", "causalChain": ["leak", "burn"], "suspectedService": "checkout-service", "supportingEvidenceIds": ["e1"] },
            { "id": "h2", "statement": "Load.", "causalChain": ["load", "burn"] }
          ]
        }
        """;

    [Fact]
    public void Valid_output_is_accepted_with_Sentinel_identifiers()
    {
        ValidatedFindings findings = FindingsValidator.Validate(Valid, Returned, Calls);

        findings.IsValid.ShouldBeTrue();
        findings.Hypotheses.Count.ShouldBe(2);
        findings.Evidence.Single().HypothesisIds.Single().ShouldBe(findings.Hypotheses[0].HypothesisId);
        findings.Hypotheses[0].SupportingEvidenceIds.Single().ShouldBe(findings.Evidence[0].EvidenceId);
    }

    [Fact]
    public void An_observation_no_tool_returned_is_refused()
    {
        ValidatedFindings findings = FindingsValidator.Validate(Valid.Replace("\"obs:2\"", "\"obs:remembered\"", StringComparison.Ordinal), Returned, Calls);

        findings.IsValid.ShouldBeFalse();
        findings.Errors.ShouldContain(error => error.Contains("obs:remembered", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("[]")]
    [InlineData("")]
    public void Output_that_is_not_a_findings_document_is_refused(string output)
    {
        FindingsValidator.Validate(output, Returned, Calls).IsValid.ShouldBeFalse();
    }

    [Fact]
    public void A_single_hypothesis_is_not_enough()
    {
        string single = Valid.Replace("""
            ,
                { "id": "h2", "statement": "Load.", "causalChain": ["load", "burn"] }
            """, string.Empty, StringComparison.Ordinal);

        FindingsValidator.Validate(single, Returned, Calls).Errors.ShouldContain(error => error.Contains("competing hypotheses", StringComparison.Ordinal));
    }

    [Fact]
    public void Strength_direction_and_tool_call_are_checked()
    {
        string broken = Valid
            .Replace("\"strength\": 0.8", "\"strength\": 1.7", StringComparison.Ordinal)
            .Replace("\"supports\"", "\"proves\"", StringComparison.Ordinal)
            .Replace("\"call_1\"", "\"call_9\"", StringComparison.Ordinal);

        ValidatedFindings findings = FindingsValidator.Validate(broken, Returned, Calls);

        findings.Errors.Count.ShouldBeGreaterThanOrEqualTo(3);
    }

    [Fact]
    public void An_explicit_abstention_is_honoured()
    {
        ValidatedFindings findings = FindingsValidator.Validate("""{ "abstain": true, "abstentionReason": "Traces are missing." }""", Returned, Calls);

        findings.Abstained.ShouldBeTrue();
        findings.AbstentionReason.ShouldBe("Traces are missing.");
    }
}
