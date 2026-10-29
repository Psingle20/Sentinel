using System.Net.Http.Headers;
using System.Net.Http.Json;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Reference.Scenarios.Api.Contracts;
using Reference.Scenarios.Api.Manifests;
using Reference.Scenarios.Application.Runs;
using Reference.Scenarios.Domain.Scenarios;

namespace Sentinel.EndToEnd.Tests;

public static class Eventually
{
    public static async Task<T> ValueAsync<T>(Func<Task<T>> read, Func<T, bool> condition, TimeSpan timeout, string description, TimeSpan? interval = null)
    {
        DateTime deadline = DateTime.UtcNow + timeout;
        T value = await read();
        while (!condition(value))
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException($"Timed out after {timeout} waiting until {description}.");
            }

            await Task.Delay(interval ?? TimeSpan.FromSeconds(2));
            value = await read();
        }

        return value;
    }
}
