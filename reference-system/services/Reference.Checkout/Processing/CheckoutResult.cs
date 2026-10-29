using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Reference.Common.Redis;
using Reference.Common.Revisions;
using Reference.Common.Telemetry;

namespace Reference.Checkout.Processing;

public sealed record CheckoutResult(string CartId, string Outcome, string Revision);
