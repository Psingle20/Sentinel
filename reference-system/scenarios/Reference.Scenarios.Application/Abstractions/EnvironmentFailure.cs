namespace Reference.Scenarios.Application.Abstractions;

/// <summary>Why an environment operation failed.</summary>
public enum EnvironmentFailure
{
    /// <summary>The environment, or the emulator behind it, did not answer or refused the call.</summary>
    Unavailable,

    /// <summary>The environment cannot perform the operation, such as a step for a workload it does not host.</summary>
    Unsupported,

    /// <summary>The environment did not reach the expected state in time.</summary>
    Timeout,

    /// <summary>A reset finished but left something of an earlier run behind.</summary>
    PartialReset
}
