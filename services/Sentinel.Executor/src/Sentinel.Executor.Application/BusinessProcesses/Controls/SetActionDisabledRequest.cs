using CleanArchitecture.BuildingBlocks.Messaging;

namespace Sentinel.Executor.Application.BusinessProcesses.Controls;

/// <summary>An operator disables or re-enables one kind of action.</summary>
public sealed record SetActionDisabledRequest(string ActionKind, bool Disabled, string Actor) : IRequest;
