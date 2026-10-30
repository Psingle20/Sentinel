namespace Sentinel.EndToEnd.Tests;

[CollectionDefinition(Name)]
public sealed class AwsSystemCollection : ICollectionFixture<AwsSystem>
{
    public const string Name = "aws-system";
}
