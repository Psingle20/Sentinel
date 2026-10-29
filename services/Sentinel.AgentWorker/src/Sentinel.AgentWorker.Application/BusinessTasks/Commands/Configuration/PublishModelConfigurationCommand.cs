using CleanArchitecture.BuildingBlocks.Messaging;
using CleanArchitecture.BuildingBlocks.Configuration;
using Sentinel.AgentWorker.Application.ControlPlane;

namespace Sentinel.AgentWorker.Application.BusinessTasks.Commands.Configuration;

public sealed record PublishModelConfigurationCommand(ModelConfigurationDocument Document, string ExpectedVersion, string Author) : ICommand<DocumentVersion>;
