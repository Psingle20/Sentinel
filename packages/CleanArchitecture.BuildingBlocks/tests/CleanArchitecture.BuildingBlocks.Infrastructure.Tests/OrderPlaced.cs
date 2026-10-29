using CleanArchitecture.BuildingBlocks.Infrastructure.Messaging;
using CleanArchitecture.BuildingBlocks.Integration;

namespace CleanArchitecture.BuildingBlocks.Infrastructure.Tests;

public sealed record OrderPlaced(Guid OrderId, OrderChannel Channel, string? Note = null);
