using System.Globalization;

namespace Reference.Scenarios.Application.Runs;

public enum StartRunOutcome
{
    Started,
    UnknownScenario,
    RunActive,

    /// <summary>The scenario belongs to a reference environment other than the one this runner drives.</summary>
    WrongEnvironment
}
