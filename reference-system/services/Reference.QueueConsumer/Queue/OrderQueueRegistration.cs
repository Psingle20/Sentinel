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

        return services;
    }
}
