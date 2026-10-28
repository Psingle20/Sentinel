using CleanArchitecture.BuildingBlocks.Infrastructure.Messaging;
using CleanArchitecture.BuildingBlocks.Integration;
using Microsoft.Extensions.DependencyInjection;

namespace CleanArchitecture.BuildingBlocks.Infrastructure.PostgreSql;

public static class PostgresMessagingExtensions
{
    /// <summary>
    /// Stores the outbox and inbox in the service's PostgreSQL database and provides the
    /// scope's unit of work. Requires an <c>NpgsqlDataSource</c> registered for that database.
    /// </summary>
    public static MessagingBuilder AddPostgresStorage(this MessagingBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddScoped<PostgresUnitOfWork>();
        builder.Services.AddScoped<IUnitOfWork>(provider => provider.GetRequiredService<PostgresUnitOfWork>());
        builder.Services.AddScoped<IPostgresTransaction>(provider => provider.GetRequiredService<PostgresUnitOfWork>());
        builder.Services.AddScoped<IInboxStore, PostgresInboxStore>();
        builder.Services.AddSingleton<IOutboxStore, PostgresOutboxStore>();
        builder.Services.AddSingleton<IMessagingStoreInitializer, PostgresMessagingSchema>();

        return builder;
    }
}
