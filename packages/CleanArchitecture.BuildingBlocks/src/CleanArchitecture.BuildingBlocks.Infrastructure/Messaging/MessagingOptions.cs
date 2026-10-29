namespace CleanArchitecture.BuildingBlocks.Infrastructure.Messaging;

public sealed class MessagingOptions
{
    /// <summary>The producing service's name in envelopes, and its consumer name in inboxes and consumer groups.</summary>
    public string ServiceName { get; set; } = string.Empty;

    /// <summary>
    /// Envelope headers that say whose business a message is, each mapped to the name it takes as a tag on the consumer's
    /// span and as a field of the log scope its handler runs in, for example a tenant header to <c>tenant.id</c>. Headers
    /// a message does not carry are left out.
    /// </summary>
    public IDictionary<string, string> ContextHeaders { get; } = new Dictionary<string, string>(StringComparer.Ordinal);
}
