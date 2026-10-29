using System.Globalization;
using System.Net.Sockets;
using System.Text;

namespace Reference.Common.Redis;

/// <summary>Redis answered a command with an error reply. The connection itself is still in step.</summary>
public sealed class RedisReplyException(string message) : Exception(message);
