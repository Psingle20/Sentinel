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

/// <summary>Redis answered a command with an error reply. The connection itself is still in step.</summary>
public sealed class RedisReplyException(string message) : Exception(message);

/// <summary>
/// One raw Redis connection speaking RESP2. The reference services use it instead of a pooled client
/// library so that every connection, and every leak, is explicit and carries a client name.
/// </summary>
public sealed class RespConnection : IAsyncDisposable
{
    private readonly TcpClient _client;
    private readonly NetworkStream _stream;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private volatile bool _broken;

    private RespConnection(TcpClient client)
    {
        _client = client;
        _stream = client.GetStream();
    }

    /// <summary>
    /// False once the socket closed or a command was interrupted part-way: an unread reply would
    /// shift every later reply by one, so an interrupted connection is never reused.
    /// </summary>
    public bool IsConnected => !_broken && _client.Connected;

    /// <summary>Connects and names the connection; fails when Redis refuses new clients.</summary>
    public static async Task<RespConnection> OpenAsync(RedisEndpoint endpoint, string clientName, TimeSpan timeout, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        var client = new TcpClient { NoDelay = true };
        RespConnection? connection = null;
        try
        {
            using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutSource.CancelAfter(timeout);
            await client.ConnectAsync(endpoint.Host, endpoint.Port, timeoutSource.Token);
            connection = new RespConnection(client);
            await connection.ExecuteAsync(timeout, cancellationToken, "CLIENT", "SETNAME", clientName);
            return connection;
        }
        catch (Exception exception) when (exception is SocketException or IOException or OperationCanceledException && !cancellationToken.IsCancellationRequested)
        {
            await DisposeQuietlyAsync(connection, client);
            throw new RedisUnavailableException($"Could not open a Redis connection for {clientName}.", exception);
        }
        catch
        {
            await DisposeQuietlyAsync(connection, client);
            throw;
        }
    }

    /// <summary>Runs one command and returns its reply as text; integers are returned as their decimal form.</summary>
    public async Task<string?> ExecuteAsync(TimeSpan timeout, CancellationToken cancellationToken, params string[] arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutSource.CancelAfter(timeout);
            await _stream.WriteAsync(Encode(arguments), timeoutSource.Token);
            return await ReadReplyAsync(timeoutSource.Token);
        }
        catch (RedisReplyException exception)
        {
            throw new RedisUnavailableException($"Redis refused {arguments[0]}: {exception.Message}", exception);
        }
        catch (Exception exception) when (exception is SocketException or IOException or OperationCanceledException)
        {
            _broken = true;
            if (cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            throw new RedisUnavailableException($"Redis command {arguments[0]} failed.", exception);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _stream.DisposeAsync();
        _client.Dispose();
        _gate.Dispose();
    }

    private static async Task DisposeQuietlyAsync(RespConnection? connection, TcpClient client)
    {
        if (connection is not null)
        {
            await connection.DisposeAsync();
        }
        else
        {
            client.Dispose();
        }
    }

    private static byte[] Encode(string[] arguments)
    {
        var builder = new StringBuilder();
        builder.Append(CultureInfo.InvariantCulture, $"*{arguments.Length}\r\n");
        foreach (string argument in arguments)
        {
            builder.Append(CultureInfo.InvariantCulture, $"${Encoding.UTF8.GetByteCount(argument)}\r\n{argument}\r\n");
        }

        return Encoding.UTF8.GetBytes(builder.ToString());
    }

    private async Task<string?> ReadReplyAsync(CancellationToken cancellationToken)
    {
        string line = await ReadLineAsync(cancellationToken);
        char kind = line[0];
        string body = line[1..];
        switch (kind)
        {
            case '+':
            case ':':
                return body;
            case '-':
                throw new RedisReplyException(body);
            case '$':
                int length = int.Parse(body, CultureInfo.InvariantCulture);
                if (length < 0)
                {
                    return null;
                }

                byte[] buffer = new byte[length + 2];
                await _stream.ReadExactlyAsync(buffer, cancellationToken);
                return Encoding.UTF8.GetString(buffer, 0, length);
            case '*':
                int count = int.Parse(body, CultureInfo.InvariantCulture);
                var items = new List<string?>();
                for (int index = 0; index < count; index++)
                {
                    items.Add(await ReadReplyAsync(cancellationToken));
                }

                return string.Join('\n', items);
            default:
                throw new RedisUnavailableException($"Unexpected Redis reply type '{kind}'.");
        }
    }

    private async Task<string> ReadLineAsync(CancellationToken cancellationToken)
    {
        var bytes = new List<byte>(64);
        byte[] one = new byte[1];
        while (true)
        {
            int read = await _stream.ReadAsync(one, cancellationToken);
            if (read == 0)
            {
                throw new IOException("Redis closed the connection.");
            }

            if (one[0] == '\n' && bytes.Count > 0 && bytes[^1] == '\r')
            {
                return Encoding.UTF8.GetString(bytes.ToArray(), 0, bytes.Count - 1);
            }

            bytes.Add(one[0]);
        }
    }
}
