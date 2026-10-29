using Sentinel.Contracts;
using Sentinel.Contracts.Incidents;
using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Gateway.Application.Live;

public enum NotificationStatus
{
    Pending,
    Delivered,
    Failed
}
