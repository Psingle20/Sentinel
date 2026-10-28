namespace Reference.Common.Redis;

/// <summary>
/// A fixed-size pool whose connections are evicted by a housekeeping timer once they reach
/// <see cref="MaxLifetime"/>, as pools with a connection lifetime do. An evicted slot is refilled
/// on its next use. When Redis is saturated, another client takes the freed connection slot in
/// between, the refill is refused and the request fails: that is how S2 hurts a healthy service.
/// </summary>
public sealed class RedisConnectionPool : IAsyncDisposable
{
    private static readonly TimeSpan HousekeepingInterval = TimeSpan.FromSeconds(1);

    private readonly RedisEndpoint _endpoint;
    private readonly string _clientName;
    private readonly TimeSpan _timeout;
    private readonly TimeProvider _time;
    private readonly RespConnection?[] _slots;
    private readonly DateTimeOffset[] _openedAt;
    private readonly SemaphoreSlim[] _slotGates;
    private readonly ITimer _housekeeping;
    private int _next;

    public RedisConnectionPool(RedisEndpoint endpoint, string clientName, int size, TimeSpan maxLifetime, TimeSpan timeout, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(time);

        _endpoint = endpoint;
        _clientName = clientName;
        _timeout = timeout;
        _time = time;
        MaxLifetime = maxLifetime;
        _slots = new RespConnection?[size];
        _openedAt = new DateTimeOffset[size];
        _slotGates = [.. Enumerable.Range(0, size).Select(_ => new SemaphoreSlim(1, 1))];
        _housekeeping = time.CreateTimer(state => _ = EvictExpiredAsync(), null, HousekeepingInterval, HousekeepingInterval);
    }

    public TimeSpan MaxLifetime { get; }

    /// <summary>Open connections right now.</summary>
    public int OpenConnections => _slots.Count(slot => slot is { IsConnected: true });

    /// <summary>Runs <paramref name="arguments"/> on one pooled connection, opening it if the slot is empty.</summary>
    public async Task<string?> ExecuteAsync(CancellationToken cancellationToken, params string[] arguments)
    {
        int index = (int)((uint)Interlocked.Increment(ref _next) % (uint)_slots.Length);
        SemaphoreSlim gate = _slotGates[index];
        await gate.WaitAsync(cancellationToken);
        try
        {
            RespConnection connection = await GetOrOpenAsync(index, cancellationToken);
            try
            {
                return await connection.ExecuteAsync(_timeout, cancellationToken, arguments);
            }
            catch (Exception exception) when (exception is RedisUnavailableException || !connection.IsConnected)
            {
                await CloseSlotAsync(index);
                throw;
            }
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>Closes connections older than <see cref="MaxLifetime"/> that are not in use.</summary>
    public async Task EvictExpiredAsync()
    {
        for (int index = 0; index < _slots.Length; index++)
        {
            if (!await _slotGates[index].WaitAsync(0))
            {
                continue;
            }

            try
            {
                if (_slots[index] is not null && _time.GetUtcNow() - _openedAt[index] >= MaxLifetime)
                {
                    await CloseSlotAsync(index);
                }
            }
            finally
            {
                _slotGates[index].Release();
            }
        }
    }

    /// <summary>Closes every connection, as a process restart would.</summary>
    public async Task ResetAsync()
    {
        for (int index = 0; index < _slots.Length; index++)
        {
            await _slotGates[index].WaitAsync();
            try
            {
                await CloseSlotAsync(index);
            }
            finally
            {
                _slotGates[index].Release();
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _housekeeping.DisposeAsync();
        await ResetAsync();
        foreach (SemaphoreSlim gate in _slotGates)
        {
            gate.Dispose();
        }
    }

    private async Task<RespConnection> GetOrOpenAsync(int index, CancellationToken cancellationToken)
    {
        if (_slots[index] is { IsConnected: true } current)
        {
            return current;
        }

        await CloseSlotAsync(index);
        RespConnection opened = await RespConnection.OpenAsync(_endpoint, _clientName, _timeout, cancellationToken);
        _slots[index] = opened;
        _openedAt[index] = _time.GetUtcNow();
        return opened;
    }

    private async Task CloseSlotAsync(int index)
    {
        RespConnection? current = _slots[index];
        _slots[index] = null;
        if (current is not null)
        {
            await current.DisposeAsync();
        }
    }
}
