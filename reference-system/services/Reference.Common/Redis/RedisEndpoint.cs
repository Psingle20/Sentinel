using System.Globalization;

namespace Reference.Common.Redis;

/// <summary>The reference Redis, configured as <c>Reference:Redis</c> = <c>host:port</c>.</summary>
public sealed record RedisEndpoint(string Host, int Port)
{
    public static RedisEndpoint Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException("Reference:Redis must be set to the reference Redis host:port.");
        }

        int separator = value.LastIndexOf(':');
        if (separator <= 0 || !int.TryParse(value[(separator + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out int port))
        {
            throw new InvalidOperationException($"Reference:Redis value '{value}' is not host:port.");
        }

        return new RedisEndpoint(value[..separator], port);
    }
}
