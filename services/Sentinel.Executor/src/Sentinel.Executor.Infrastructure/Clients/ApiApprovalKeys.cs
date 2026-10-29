using System.Buffers.Text;
using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Sentinel.Contracts.Messaging;
using Sentinel.Contracts.Remediation;
using Sentinel.Executor.Application;
using Sentinel.Executor.Domain.Capabilities;
using Sentinel.Executor.Application.Abstractions.Security;

namespace Sentinel.Executor.Infrastructure.Clients;

/// <summary>
/// Resolves approval keys from Api's published key set, caching them. An unknown key ID triggers a
/// refresh at most every 10 seconds, which picks up rotation without letting forged IDs flood Api.
/// </summary>
internal sealed class ApiApprovalKeys(IHttpClientFactory clients, TimeProvider time, ILogger<ApiApprovalKeys> logger) : IApprovalKeyResolver, IDisposable
{
    public const string ClientName = "approval-keys";

    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(10);

    private readonly ConcurrentDictionary<string, ECDsa> _keys = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _refresh = new(1, 1);
    private DateTimeOffset _lastRefresh = DateTimeOffset.MinValue;

    public async Task<ECDsa?> ResolveAsync(string keyId, CancellationToken cancellationToken)
    {
        if (_keys.TryGetValue(keyId, out ECDsa? key))
        {
            return key;
        }

        await _refresh.WaitAsync(cancellationToken);
        try
        {
            if (!_keys.ContainsKey(keyId) && time.GetUtcNow() - _lastRefresh >= RefreshInterval)
            {
                _lastRefresh = time.GetUtcNow();
                using HttpClient http = clients.CreateClient(ClientName);
                ApprovalKeySet? set = await http.GetFromJsonAsync<ApprovalKeySet>(ApprovalAttestation.KeysPath, SentinelJson.Options, cancellationToken);
                foreach (ApprovalVerificationKey published in set?.Keys ?? [])
                {
                    if (published is { KeyType: "EC", Curve: "P-256", Algorithm: ApprovalAttestation.Algorithm } && !_keys.ContainsKey(published.KeyId))
                    {
                        _keys[published.KeyId] = ECDsa.Create(new ECParameters
                        {
                            Curve = ECCurve.NamedCurves.nistP256,
                            Q = new ECPoint { X = Base64Url.DecodeFromChars(published.X), Y = Base64Url.DecodeFromChars(published.Y) }
                        });
                    }
                }

                logger.LogInformation("Loaded {Count} approval verification keys from Api", _keys.Count);
            }
        }
        catch (HttpRequestException exception)
        {
            logger.LogWarning(exception, "Approval keys could not be loaded from Api");
        }
        finally
        {
            _refresh.Release();
        }

        return _keys.GetValueOrDefault(keyId);
    }

    public void Dispose()
    {
        foreach (ECDsa key in _keys.Values)
        {
            key.Dispose();
        }

        _refresh.Dispose();
    }
}
