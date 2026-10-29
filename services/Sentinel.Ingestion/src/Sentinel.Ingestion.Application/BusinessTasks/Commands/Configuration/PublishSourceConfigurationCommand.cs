using CleanArchitecture.BuildingBlocks.Messaging;
using CleanArchitecture.BuildingBlocks.Configuration;
using Sentinel.Ingestion.Application.ControlPlane;

namespace Sentinel.Ingestion.Application.BusinessTasks.Commands.Configuration;

public sealed record PublishSourceConfigurationCommand(SourceConfigurationDocument Document, string ExpectedVersion, string Author) : ICommand<DocumentVersion>;
