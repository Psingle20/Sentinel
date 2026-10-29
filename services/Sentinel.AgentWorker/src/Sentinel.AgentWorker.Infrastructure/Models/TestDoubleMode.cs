using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace Sentinel.AgentWorker.Infrastructure.Models;

public enum TestDoubleMode
{
    /// <summary>A fixed, rule-based analysis of the tool results.</summary>
    Deterministic,

    /// <summary>Returns text that is not JSON.</summary>
    Malformed,

    /// <summary>Returns valid JSON with a single hypothesis.</summary>
    Incomplete,

    /// <summary>Cites an observation ID no tool returned.</summary>
    Fabricated,

    Refusal,

    /// <summary>Never answers, so the investigation's time budget expires.</summary>
    Timeout,

    /// <summary>
    /// Behaves like a model that obeys instructions planted in tool results: when a result asks it to,
    /// it adds actions and a certain, uncited conclusion. The guardrail tests use it to prove such
    /// output is refused, never obeyed.
    /// </summary>
    InjectionSusceptible
}
