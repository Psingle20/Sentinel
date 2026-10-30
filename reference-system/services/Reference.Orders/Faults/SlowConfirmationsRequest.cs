namespace Reference.Orders.Faults;

/// <summary>Slow order confirmations for <see cref="Seconds"/>.</summary>
public sealed record SlowConfirmationsRequest(int Seconds = 180);
