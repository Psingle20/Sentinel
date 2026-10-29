using CleanArchitecture.BuildingBlocks.Infrastructure.Messaging;
using CleanArchitecture.BuildingBlocks.Infrastructure.PostgreSql;
using CleanArchitecture.BuildingBlocks.Infrastructure.Redis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using StackExchange.Redis;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;

namespace CleanArchitecture.BuildingBlocks.Infrastructure.IntegrationTests;

[CollectionDefinition(Name)]
public sealed class MessagingCollection : ICollectionFixture<MessagingInfrastructure>
{
    public const string Name = "messaging";
}
