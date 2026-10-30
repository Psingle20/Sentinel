using CleanArchitecture.BuildingBlocks.Messaging;

namespace Reference.Scenarios.Application.BusinessProcesses.Environments;

/// <summary>Creates whatever the reference environment still lacks; a run's reset does the same first.</summary>
public sealed record ProvisionEnvironmentRequest : IRequest;
