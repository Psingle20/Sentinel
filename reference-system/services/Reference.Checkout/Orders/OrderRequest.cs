using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Reference.Common.Redis;
using Reference.Common.Revisions;
using Reference.Common.Telemetry;

namespace Reference.Checkout.Orders;

public sealed record OrderRequest(string CartId);
