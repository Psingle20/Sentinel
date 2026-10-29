using System.Buffers.Text;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using Sentinel.Contracts.Messaging;
using Sentinel.Contracts.Remediation;
using Sentinel.Executor.Application.Abstractions.Security;

namespace Sentinel.Executor.Infrastructure.Clients;

/// <summary>
/// Resolves approval keys from the gateway's published key set (ADR-006). The set is read again after
/// <see cref="KeySetLifetime"/>, so a key the gateway retires stops verifying within that time; each key is trusted only
/// inside its own <c>nbf</c>/<c>exp</c> window; and a set that could not be read for <see cref="MaxStaleness"/> trusts no
/// key, because whether a key is still published can no longer be known. An unknown key ID triggers a refresh at most
/// every 10 seconds, which picks up rotation without letting forged IDs flood the gateway.
/// </summary>
internal sealed class ApiApprovalKeys(IHttpClientFactory clients, TimeProvider time, ILogger<ApiApprovalKeys> logger) : IApprovalKeyResolver, IDisposable
{
    public const string ClientName = "approval-keys";

    public static readonly TimeSpan KeySetLifetime = TimeSpan.FromMinutes(5);

    public static readonly TimeSpan MaxStaleness = TimeSpan.FromHours(1);

    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(10);

    private readonly SemaphoreSlim _refresh = new(1, 1);

    // Keys dropped from the set may still be verifying a request on another thread; they are disposed with the resolver.
    private readonly List<ECDsa> _retired = [];
    private KeySet _set = KeySet.Empty;
    private DateTimeOffset _lastAttempt = DateTimeOffset.MinValue;

    public async Task<ECDsa?> ResolveAsync(string keyId, CancellationToken cancellationToken)
    {
        DateTimeOffset now = time.GetUtcNow();
        KeySet set = Volatile.Read(ref _set);
        bool expired = now - set.LoadedAt >= KeySetLifetime;
        if (expired || !set.Keys.ContainsKey(keyId))
        {
            set = await RefreshAsync(now, cancellationToken);
        }

        if (now - set.LoadedAt >= MaxStaleness)
        {
            logger.LogWarning("Approval keys were last read at {LoadedAt}; no key is trusted until the gateway's key set can be read again", set.LoadedAt);
            return null;
        }

        return set.Keys.TryGetValue(keyId, out PublishedKey? key) && key.ValidAt(now) ? key.Verifier : null;
    }

    public void Dispose()
    {
        foreach (PublishedKey key in _set.Keys.Values)
        {
            key.Verifier.Dispose();
        }

        foreach (ECDsa key in _retired)
        {
            key.Dispose();
        }

        _refresh.Dispose();
    }

    private async Task<KeySet> RefreshAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        await _refresh.WaitAsync(cancellationToken);
        try
        {
            // Another caller read the set moments ago, or a failed read is still recent: use what is there.
            KeySet current = _set;
            if (now - _lastAttempt < RefreshInterval)
            {
                return current;
            }

            _lastAttempt = now;
            using HttpClient http = clients.CreateClient(ClientName);
            ApprovalKeySet? published = await http.GetFromJsonAsync<ApprovalKeySet>(ApprovalAttestation.KeysPath, SentinelJson.Options, cancellationToken);
            var keys = new Dictionary<string, PublishedKey>(StringComparer.Ordinal);
            foreach (ApprovalVerificationKey key in published?.Keys ?? [])
            {
                if (key is not { KeyType: "EC", Curve: "P-256", Algorithm: ApprovalAttestation.Algorithm } || keys.ContainsKey(key.KeyId))
                {
                    continue;
                }

                // A key published unchanged keeps its verifier; a changed or new one gets a new verifier.
                ECDsa verifier = current.Keys.TryGetValue(key.KeyId, out PublishedKey? known) && known.X == key.X && known.Y == key.Y
                    ? known.Verifier
                    : ECDsa.Create(new ECParameters
                    {
                        Curve = ECCurve.NamedCurves.nistP256,
                        Q = new ECPoint { X = Base64Url.DecodeFromChars(key.X), Y = Base64Url.DecodeFromChars(key.Y) }
                    });
                keys[key.KeyId] = new PublishedKey(verifier, key.X, key.Y, DateTimeOffset.FromUnixTimeSeconds(key.NotBefore), key.Expires is { } expires ? DateTimeOffset.FromUnixTimeSeconds(expires) : null);
            }

            _retired.AddRange(current.Keys.Values.Where(old => !keys.Values.Any(kept => ReferenceEquals(kept.Verifier, old.Verifier))).Select(old => old.Verifier));
            var refreshed = new KeySet(keys, now);
            Volatile.Write(ref _set, refreshed);
            logger.LogInformation("Loaded {Count} approval verification keys from the gateway", keys.Count);
            return refreshed;
        }
        catch (HttpRequestException exception)
        {
            logger.LogWarning(exception, "Approval keys could not be loaded from the gateway");
            return _set;
        }
        finally
        {
            _refresh.Release();
        }
    }

    private sealed record KeySet(IReadOnlyDictionary<string, PublishedKey> Keys, DateTimeOffset LoadedAt)
    {
        public static readonly KeySet Empty = new(new Dictionary<string, PublishedKey>(StringComparer.Ordinal), DateTimeOffset.MinValue);
    }

    private sealed record PublishedKey(ECDsa Verifier, string X, string Y, DateTimeOffset NotBefore, DateTimeOffset? Expires)
    {
        public bool ValidAt(DateTimeOffset now) => now >= NotBefore && (Expires is null || now < Expires);
    }
}
