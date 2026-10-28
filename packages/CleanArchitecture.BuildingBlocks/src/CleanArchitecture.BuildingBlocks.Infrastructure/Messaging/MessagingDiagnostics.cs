using System.Diagnostics;

namespace CleanArchitecture.BuildingBlocks.Infrastructure.Messaging;

internal static class MessagingDiagnostics
{
    internal const string ActivitySourceName = "CleanArchitecture.BuildingBlocks.Messaging";

    internal static readonly ActivitySource ActivitySource = new(ActivitySourceName);

    /// <summary>Writes the current trace context (traceparent, tracestate) into outgoing headers.</summary>
    internal static void InjectTraceContext(Dictionary<string, string> headers)
    {
        if (Activity.Current is null)
        {
            return;
        }

        DistributedContextPropagator.Current.Inject(
            Activity.Current,
            headers,
            static (carrier, key, value) => ((Dictionary<string, string>)carrier!)[key] = value);
    }

    /// <summary>Starts a consumer span whose parent is the producer's trace context from the headers.</summary>
    internal static Activity? StartConsumerActivity(MessageEnvelope envelope)
    {
        DistributedContextPropagator.Current.ExtractTraceIdAndState(
            envelope.Headers,
            static (object? carrier, string name, out string? value, out IEnumerable<string>? values) =>
            {
                values = null;
                value = ((IReadOnlyDictionary<string, string>)carrier!).GetValueOrDefault(name);
            },
            out string? traceParent,
            out string? traceState);

        ActivityContext.TryParse(traceParent, traceState, out ActivityContext parent);

        Activity? activity = ActivitySource.StartActivity($"process {envelope.MessageType}", ActivityKind.Consumer, parent);
        activity?.SetTag("messaging.message.id", envelope.MessageId.ToString());
        activity?.SetTag("messaging.message.type", envelope.MessageType);
        activity?.SetTag("messaging.message.conversation_id", envelope.CorrelationId);
        return activity;
    }

    internal static bool IsTraceHeader(string name) =>
        string.Equals(name, "traceparent", StringComparison.OrdinalIgnoreCase)
        || string.Equals(name, "tracestate", StringComparison.OrdinalIgnoreCase);
}
