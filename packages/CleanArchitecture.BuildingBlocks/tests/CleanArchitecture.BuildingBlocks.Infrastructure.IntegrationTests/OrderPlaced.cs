using CleanArchitecture.BuildingBlocks.Infrastructure.Messaging;
using CleanArchitecture.BuildingBlocks.Infrastructure.PostgreSql;
using Npgsql;

namespace CleanArchitecture.BuildingBlocks.Infrastructure.IntegrationTests;

public sealed record OrderPlaced(Guid OrderId, decimal Amount);
