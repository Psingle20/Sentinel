using System.Globalization;

namespace Sentinel.Executor.Domain.Policy;

/// <summary>
/// Checks a policy document before it may be published (Phase 08 control plane): a document that
/// would silently change behaviour, such as a duplicate rule ID, an unknown risk or a freeze that
/// ends before it starts, is refused with every problem listed.
/// </summary>
public static class PolicyValidator
{
    private static readonly string[] Risks = ["low", "medium", "high"];
    private static readonly string[] Severities = ["low", "medium", "high", "critical"];

    public static IReadOnlyList<string> Problems(PolicyDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var problems = new List<string>();
        if (string.IsNullOrWhiteSpace(document.Version))
        {
            problems.Add("version is required.");
        }

        foreach (string duplicate in document.Rules.GroupBy(rule => rule.Id, StringComparer.Ordinal).Where(group => group.Count() > 1).Select(group => group.Key))
        {
            problems.Add($"rule ID '{duplicate}' is used more than once.");
        }

        foreach (PolicyRule rule in document.Rules)
        {
            if (string.IsNullOrWhiteSpace(rule.Id) || string.IsNullOrWhiteSpace(rule.Action))
            {
                problems.Add("every rule needs an id and an action.");
            }

            if (rule.Environments.Count == 0)
            {
                problems.Add($"rule '{rule.Id}' covers no environment.");
            }

            if (!Risks.Contains(rule.MaxRisk))
            {
                problems.Add($"rule '{rule.Id}' has unknown maxRisk '{rule.MaxRisk}'.");
            }

            if (rule.MinConfidence is < 0 or > 1)
            {
                problems.Add(string.Create(CultureInfo.InvariantCulture, $"rule '{rule.Id}' has minConfidence {rule.MinConfidence}; it must be between 0 and 1."));
            }

            if (rule.MaxBlastRadius < 1)
            {
                problems.Add($"rule '{rule.Id}' has maxBlastRadius below 1.");
            }

            if (rule.AutonomousFromSeverity is { } severity && !Severities.Contains(severity))
            {
                problems.Add($"rule '{rule.Id}' has unknown autonomousFromSeverity '{severity}'.");
            }
        }

        foreach (ChangeFreeze freeze in document.Freezes)
        {
            if (freeze.Until <= freeze.From)
            {
                problems.Add($"freeze '{freeze.Id}' ends before it starts.");
            }

            if (freeze.Environments.Count == 0)
            {
                problems.Add($"freeze '{freeze.Id}' covers no environment.");
            }
        }

        return problems;
    }
}
