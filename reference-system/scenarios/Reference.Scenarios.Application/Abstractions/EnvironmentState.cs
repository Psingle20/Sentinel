namespace Reference.Scenarios.Application.Abstractions;

/// <summary>
/// What an environment reports about itself: whether it is ready for a run, what is wrong if not, and, for an emulated
/// one, the emulator and image digest it runs on and the provisioning version, so a run record can name them.
/// </summary>
public sealed record EnvironmentState(
    string Environment,
    bool Ready,
    IReadOnlyList<string> Problems,
    string? Emulator = null,
    string? ImageDigest = null,
    string? ProvisioningVersion = null);
