using System.Diagnostics.CodeAnalysis;

namespace CleanArchitecture.BuildingBlocks.Infrastructure.Messaging;

public sealed record MessageRegistration(Type ClrType, string MessageType, int SchemaVersion, string Stream);
