namespace Sentinel.EndToEnd.Tests;

/// <summary>Sentinel with the AWS reference environment (Phase E01, Stage 2): the queue-consumer on ECS and SQS, its changes read from ECS.</summary>
public sealed class AwsSystem : SentinelSystem
{
    public AwsSystem()
        : base(referenceOnly: false, referenceEnvironment: "aws")
    {
    }
}
