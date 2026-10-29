using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Configuration;
using Sentinel.Gateway.Application.Live;
using Sentinel.Contracts;
using Sentinel.Contracts.Incidents;

namespace Sentinel.Gateway.Application.ControlPlane;

public sealed record NotificationChannelSetting(string Name, string Kind, Uri? Url, string? SecretKey);
