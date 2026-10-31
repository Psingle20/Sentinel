using Amazon.EventBridge;
using Amazon.Runtime;
using Amazon.SQS;

namespace Reference.QueueConsumer.Queue;

public static class OrderQueueRegistration
{
    /// <summary>
    /// The orders queue <c>Reference:Queue:Provider</c> names: <c>redis</c>, the default, or <c>sqs</c> in the AWS
    /// reference environment. The SQS client takes its endpoint and synthetic key from configuration and never falls
    /// back to AWS's own endpoints or the machine's credential chain.
    /// </summary>
    public static IServiceCollection AddOrderQueue(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        string provider = configuration["Reference:Queue:Provider"] ?? "redis";
        switch (provider)
        {
            case "redis":
                services.AddSingleton<RedisOrderQueue>();
                services.AddSingleton<IOrderQueue>(provider => provider.GetRequiredService<RedisOrderQueue>());
                break;
            case "sqs":
                SqsQueueOptions options = configuration.GetSection(SqsQueueOptions.Section).Get<SqsQueueOptions>() ?? new SqsQueueOptions();
                Uri endpoint = options.ServiceUrl ?? throw new InvalidOperationException($"{SqsQueueOptions.Section}:ServiceUrl must name the emulator's endpoint.");
                services.AddSingleton(options);
                services.AddSingleton<IAmazonSQS>(_ => new AmazonSQSClient(
                    new BasicAWSCredentials(options.AccessKey, options.SecretKey),
                    new AmazonSQSConfig { ServiceURL = endpoint.ToString(), AuthenticationRegion = options.Region }));
                services.AddSingleton<IOrderQueue, SqsOrderQueue>();
                break;
            default:
                throw new InvalidOperationException($"Reference:Queue:Provider is '{provider}'; it must be 'redis' or 'sqs'.");
        }

        return services.AddOrderPublisher(configuration);
    }

    /// <summary>
    /// How the producer publishes, from <c>Reference:Queue:Publish</c>: <c>queue</c>, the default, straight onto the
    /// queue, or <c>eventbridge</c> in the AWS reference environment, through an EventBridge rule that routes orders to it.
    /// </summary>
    private static IServiceCollection AddOrderPublisher(this IServiceCollection services, IConfiguration configuration)
    {
        string publish = configuration["Reference:Queue:Publish"] ?? "queue";
        switch (publish)
        {
            case "queue":
                services.AddSingleton<IOrderPublisher, QueueOrderPublisher>();
                break;
            case "eventbridge":
                EventBridgeOptions options = configuration.GetSection(EventBridgeOptions.Section).Get<EventBridgeOptions>() ?? new EventBridgeOptions();
                Uri endpoint = options.ServiceUrl ?? throw new InvalidOperationException($"{EventBridgeOptions.Section}:ServiceUrl must name the emulator's endpoint.");
                services.AddSingleton(options);
                services.AddSingleton<IAmazonEventBridge>(_ => new AmazonEventBridgeClient(
                    new BasicAWSCredentials(options.AccessKey, options.SecretKey),
                    new AmazonEventBridgeConfig { ServiceURL = endpoint.ToString(), AuthenticationRegion = options.Region }));
                services.AddSingleton<IOrderPublisher, EventBridgeOrderPublisher>();
                break;
            default:
                throw new InvalidOperationException($"Reference:Queue:Publish is '{publish}'; it must be 'queue' or 'eventbridge'.");
        }

        return services;
    }
}
