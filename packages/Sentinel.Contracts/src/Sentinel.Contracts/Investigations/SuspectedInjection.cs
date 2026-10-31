using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Contracts.Investigations;

/// <summary>
/// Text in an untrusted tool result that reads as an instruction to an AI agent: a log line or a runbook passage that
/// tells the investigator what to conclude or do. It is reported so that people see the attempt, and it is never obeyed:
/// tool results are data, and nothing found here becomes evidence or a decision input. Detection is a fixed set of
/// patterns, so an attempt it misses is still only data.
/// </summary>
public sealed record SuspectedInjection
{
    /// <summary>The tool whose result held the text, such as <c>log_signatures</c> or <c>search_runbooks</c>.</summary>
    public required string Tool { get; init; }

    /// <summary>Where the text came from: the observation ID of a log line, or the document ID of a runbook.</summary>
    public required string Source { get; init; }

    public ServiceId? ServiceId { get; init; }

    /// <summary>The text that matched, at most a few hundred characters of it, as the tool returned it after redaction.</summary>
    public required string Excerpt { get; init; }

    /// <summary>The pattern it matched, such as <c>ignore-instructions</c>.</summary>
    public required string Pattern { get; init; }
}
