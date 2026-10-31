using System.Globalization;
using System.Text;
using System.Text.Json;
using Reference.Scenarios.Api.Contracts;
using Reference.Scenarios.Api.Manifests;
using Reference.Scenarios.Application.Runs;
using Reference.Scenarios.Domain.Scenarios;
using Sentinel.Contracts.Incidents;
using Sentinel.Contracts.Investigations;
using Sentinel.Contracts.Messaging;
using Sentinel.Contracts.Remediation;

namespace Sentinel.EndToEnd.Tests;

/// <summary>
/// What Sentinel and the deploy-blame baseline concluded for one scenario run, scored against the
/// manifest's ground truth (Phase 01A deliverable 6, the first comparison report).
/// </summary>
public sealed record OutcomeRecord
{
    public required string ScenarioId { get; init; }

    public required string Kind { get; init; }

    public required DateTimeOffset RecordedAt { get; init; }

    public required string Investigator { get; init; }

    public required string ExpectedCause { get; init; }

    public required string ExpectedRemediation { get; init; }

    public required string SentinelDecision { get; init; }

    public string? SentinelCause { get; init; }

    public string? SentinelPlan { get; init; }

    public required string SentinelIncidentStatus { get; init; }

    public string? SentinelRecovery { get; init; }

    public required bool SentinelCorrect { get; init; }

    public required bool SentinelFalseRemediation { get; init; }

    public required string BaselineDecision { get; init; }

    public string? BaselinePlan { get; init; }

    public required bool BaselineCorrect { get; init; }

    public required bool BaselineFalseRemediation { get; init; }

    public required bool Approved { get; init; }

    private static readonly JsonSerializerOptions Indented = new(SentinelJson.Options) { WriteIndented = true };

    public static OutcomeRecord From(ScenarioManifest manifest, IncidentDetail incident, InvestigationCompleted? baseline, bool approved)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(incident);

        string expectedService = manifest.Hypotheses?.Leading.Service ?? "none";
        bool abstentionAcceptable = manifest.Remediation.Permitted.All(remedy => remedy.Mode == "suggestion-only");

        HypothesisView? leader = incident.Decision?.LeadingHypothesisId is { } leading
            ? incident.Hypotheses.FirstOrDefault(hypothesis => hypothesis.Finding.HypothesisId == leading)
            : null;
        RemediationAction? planned = incident.Plan?.Plan.Actions is [var first, ..] ? first : null;
        bool diagnosed = incident.Decision?.Outcome == DecisionOutcome.Diagnosed;

        // The right service is not enough: in S6 the innocent deployment went to the very service the poison message hurts,
        // so an explanation is right only when it blames a change exactly when the injected cause is one.
        bool causeIsDeployment = manifest.Steps.Any(step => step.Role == "cause" && step.Action == "deploy");
        bool leaderIsChange = leader?.Finding.SuspectedChangeObservationId is not null;

        HypothesisFinding? blamed = baseline is { Outcome: InvestigationOutcome.Completed } && baseline.Hypotheses.Count > 0 ? baseline.Hypotheses[0] : null;

        return new OutcomeRecord
        {
            ScenarioId = manifest.Id,
            Kind = manifest.Kind,
            RecordedAt = DateTimeOffset.UtcNow,
            Investigator = incident.Investigations.Count > 0 && incident.Investigations[0].Model is { } model ? $"{model.Provider}/{model.Model}" : "unknown",
            ExpectedCause = manifest.Hypotheses?.Leading.Cause ?? "no incident",
            ExpectedRemediation = string.Join("; ", manifest.Remediation.Permitted.Select(remedy => $"{remedy.Action} {remedy.Service} ({remedy.Mode})")),
            SentinelDecision = incident.Decision?.Outcome.ToString().ToLowerInvariant() ?? "none",
            SentinelCause = leader?.Finding.Statement,
            SentinelPlan = planned switch
            {
                RollbackAction rollback => $"rollback {rollback.TargetServiceId} {rollback.FromRevision} -> {rollback.ToRevision}",
                PauseBatchAction pause => $"pause-batch {pause.TargetServiceId}",
                QuarantineMessageAction quarantine => $"quarantine {quarantine.TargetServiceId} {quarantine.Queue} messages received {quarantine.MinimumReceives}+ times",
                RestoreRouteAction restore => $"restore the {restore.Rule} route to {restore.TargetServiceId}",
                _ => null
            },
            SentinelIncidentStatus = incident.Summary.Status.ToString(),
            SentinelRecovery = incident.Recovery?.Outcome.ToString(),
            SentinelCorrect = diagnosed ? leader?.Finding.SuspectedServiceId?.Value == expectedService && leaderIsChange == causeIsDeployment : abstentionAcceptable,
            SentinelFalseRemediation = planned is RollbackAction rolledBack && IsFalse(manifest, rolledBack.TargetServiceId.Value),
            BaselineDecision = blamed is null ? "abstained" : $"blame {blamed.SuspectedServiceId}",
            BaselinePlan = blamed?.SuspectedServiceId is { } service ? $"rollback {service}" : null,
            BaselineCorrect = blamed?.SuspectedServiceId is { } blamedService ? blamedService.Value == expectedService && causeIsDeployment : abstentionAcceptable,
            BaselineFalseRemediation = blamed?.SuspectedServiceId is { } target && IsFalse(manifest, target.Value),
            Approved = approved
        };
    }

    public static async Task WriteAsync(OutcomeRecord record, IncidentDetail incident)
    {
        ArgumentNullException.ThrowIfNull(record);

        string directory = ReportDirectory();
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(Path.Combine(directory, $"{record.ScenarioId}.outcome.json"), JsonSerializer.Serialize(record, Indented) + "\n");
        await File.WriteAllTextAsync(Path.Combine(directory, $"{record.ScenarioId}.run-record.json"), JsonSerializer.Serialize(new IncidentRunRecord(1, incident, record.RecordedAt), Indented) + "\n");

        OutcomeRecord[] records = [.. Directory.EnumerateFiles(directory, "*.outcome.json")
            .Select(path => JsonSerializer.Deserialize<OutcomeRecord>(File.ReadAllText(path), SentinelJson.Options)!)
            .OrderBy(outcome => outcome.ScenarioId, StringComparer.Ordinal)];
        await File.WriteAllTextAsync(Path.Combine(directory, "comparison.md"), Render(records));
    }

    private static bool IsFalse(ScenarioManifest manifest, string service) =>
        manifest.Remediation.FalseRemediations.Any(remedy => remedy.Action == "rollback" && remedy.Service == service);

    private static string Render(IReadOnlyList<OutcomeRecord> records)
    {
        var report = new StringBuilder();
        report.AppendLine("# Walking skeleton and baseline comparison");
        report.AppendLine();
        report.AppendLine("Generated by `deploy/local/Sentinel.EndToEnd.Tests` (`GoldenPathTests`); regenerate with the command in this directory's README. Do not edit by hand.");
        report.AppendLine();
        report.AppendLine("| Scenario | Expected | Sentinel decision | Sentinel plan | Sentinel correct | Sentinel false remediation | Baseline | Baseline correct | Baseline false remediation |");
        report.AppendLine("| --- | --- | --- | --- | --- | --- | --- | --- | --- |");
        foreach (OutcomeRecord record in records)
        {
            report.AppendLine(CultureInfo.InvariantCulture, $"| {record.ScenarioId} ({record.Kind}) | {record.ExpectedCause} | {record.SentinelDecision}: {record.SentinelCause ?? "-"} | {record.SentinelPlan ?? "none"} | {YesNo(record.SentinelCorrect)} | {YesNo(record.SentinelFalseRemediation)} | {record.BaselineDecision} | {YesNo(record.BaselineCorrect)} | {YesNo(record.BaselineFalseRemediation)} |");
        }

        report.AppendLine();
        report.AppendLine("Investigator: " + string.Join(", ", records.Select(record => record.Investigator).Distinct()) + ". The model test double proves the pipeline, not reasoning quality (ADR-015).");
        report.AppendLine();
        report.AppendLine("Records: " + string.Join(", ", records.Select(record => $"{record.ScenarioId} at {record.RecordedAt:O}")) + ".");
        return report.ToString();

        static string YesNo(bool value) => value ? "yes" : "no";
    }

    private static string ReportDirectory()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Sentinel.slnx")))
        {
            directory = directory.Parent;
        }

        return Path.Combine(directory?.FullName ?? AppContext.BaseDirectory, "artifacts", "reports", "walking-skeleton");
    }
}
