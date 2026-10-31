namespace Reference.Tests.Aws;

/// <summary>
/// Local AWS tests, run one class at a time: each class starts a local AWS of its own, and several at once contend
/// for Docker enough to change what local AWS reports.
/// </summary>
[CollectionDefinition(Name)]
public sealed class LocalAwsCollection
{
    public const string Name = "local-aws";
}
