namespace Sentinel.Ingestion.Tests;

/// <summary>One pinned emulator for every AWS provider test, which run one class at a time against it.</summary>
[CollectionDefinition(Name)]
public sealed class AwsEmulatorCollection : ICollectionFixture<AwsEmulator>
{
    public const string Name = "aws-emulator";
}
