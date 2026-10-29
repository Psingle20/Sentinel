using CleanArchitecture.BuildingBlocks.Messaging;
using CleanArchitecture.BuildingBlocks.Configuration;
using Sentinel.Runtime.Application.ControlPlane;

namespace Sentinel.Runtime.Application.BusinessTasks.Commands.Configuration;

public sealed record PublishRuntimeConfigurationCommand(RuntimeConfigurationDocument Document, string ExpectedVersion, string Author) : ICommand<DocumentVersion>;
