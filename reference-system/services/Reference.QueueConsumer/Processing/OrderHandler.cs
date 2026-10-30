using System.Globalization;
using Reference.QueueConsumer.Queue;

namespace Reference.QueueConsumer.Processing;

/// <summary>Processes one order message: books its amount. An amount that is not a number cannot be booked.</summary>
public static class OrderHandler
{
    private static readonly TimeSpan BookingTime = TimeSpan.FromMilliseconds(40);

    public static async Task HandleAsync(OrderMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (!decimal.TryParse(message.Amount, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal amount) || amount <= 0)
        {
            throw new FormatException($"Amount '{message.Amount}' is not a positive number.");
        }

        await Task.Delay(BookingTime, cancellationToken);
    }
}
