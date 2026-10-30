namespace Reference.Tests.Aws;

/// <summary>
/// The emulator tests, run one class at a time: each class starts an emulator of its own, and several at once contend
/// for Docker enough to change what the emulator reports.
/// </summary>
[CollectionDefinition(Name)]
public sealed class EmulatorCollection
{
    public const string Name = "emulator";
}
