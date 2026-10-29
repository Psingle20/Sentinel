namespace CleanArchitecture.BuildingBlocks.Infrastructure.Messaging;

public sealed class MessagingOptions
{
    /// <summary>The producing service's name in envelopes, and its consumer name in inboxes and consumer groups.</summary>
    public string ServiceName { get; set; } = string.Empty;
}
