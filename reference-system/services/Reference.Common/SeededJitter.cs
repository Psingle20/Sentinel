using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Reference.Common.Control;
using Reference.Common.Redis;
using Reference.Common.Revisions;
using Reference.Common.Telemetry;

namespace Reference.Common;

/// <summary>Deterministic noise: the same seed gives the same sequence of jitter values.</summary>
public sealed class SeededJitter(int seed)
{
    private readonly Lock _gate = new();
    private readonly Random _random = new(seed);

    /// <summary>A value in [-1, 1).</summary>
    public double NextSigned()
    {
        lock (_gate)
        {
            return _random.NextDouble() * 2 - 1;
        }
    }

    /// <summary><paramref name="value"/> varied by up to <paramref name="fraction"/> either way.</summary>
    public TimeSpan Vary(TimeSpan value, double fraction) => value * (1 + fraction * NextSigned());
}
