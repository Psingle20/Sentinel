using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Configuration;
using Sentinel.Gateway.Application.Live;
using Sentinel.Contracts;
using Sentinel.Contracts.Incidents;

namespace Sentinel.Gateway.Application.ControlPlane;

public sealed record NotificationRouteSetting(string Event, Severity? MinimumSeverity, string Channel, string? Tenant = null);
