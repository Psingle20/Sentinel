using System.Globalization;
using System.Net.Sockets;
using System.Text;

namespace Reference.Common.Redis;

/// <summary>Redis refused or failed a command, for example because maxclients was reached.</summary>
public sealed class RedisUnavailableException : Exception
{
    public RedisUnavailableException()
    {
    }

    public RedisUnavailableException(string message)
        : base(message)
    {
    }

    public RedisUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
