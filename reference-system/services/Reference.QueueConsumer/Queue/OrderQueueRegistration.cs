namespace Reference.QueueConsumer.Queue;

public static class OrderQueueRegistration
{
    /// <summary>The orders queue <c>Reference:Queue:Provider</c> names: <c>redis</c>, the default and only one so far.</summary>
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
            default:
                throw new InvalidOperationException($"Reference:Queue:Provider is '{provider}'; it must be 'redis'.");
        }

        return services;
    }
}
