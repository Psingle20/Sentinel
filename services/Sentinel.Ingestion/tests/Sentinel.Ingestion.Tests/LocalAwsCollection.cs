namespace Sentinel.Ingestion.Tests;

/// <summary>One local AWS for every AWS provider test, which run one class at a time against it.</summary>
[CollectionDefinition(Name)]
public sealed class LocalAwsCollection : ICollectionFixture<LocalAws>
{
    public const string Name = "local-aws";
}
