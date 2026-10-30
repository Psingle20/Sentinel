using CleanArchitecture.BuildingBlocks.Messaging;
using Reference.Scenarios.Application.Abstractions;

namespace Reference.Scenarios.Application.BusinessProcesses.Environments;

/// <summary>Whether the reference environment is ready for a run, and what it runs on.</summary>
public sealed record GetEnvironmentRequest : IRequest<EnvironmentState>;
