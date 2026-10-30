namespace Reference.QueueConsumer.Queue;

/// <summary>The queue at a glance, as an operator or the quarantine adapter inspects it.</summary>
public sealed record QueueStatus(
    string Queue,
    int Waiting,
    int InFlight,
    int Quarantined,
    double OldestMessageAgeSeconds,
    int MaxReceiveCount,
    IReadOnlyList<MessageView> Redelivered);
