using System.Collections.Concurrent;
using CleanArchitecture.BuildingBlocks.Integration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Sentinel.Contracts;
using Sentinel.Contracts.Incidents;
using Sentinel.Contracts.Observations;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Contracts.Queries;
using Sentinel.Contracts.Remediation;
using Sentinel.Runtime.Application;
using Sentinel.Runtime.Application.Incidents;
using Sentinel.Runtime.Application.Memory;
using Sentinel.Runtime.Domain.Correlation;
using Sentinel.Runtime.Domain.Incidents;
using Sentinel.Runtime.Application.Abstractions.Runtime;
using Sentinel.Runtime.Application.Abstractions.Persistence;
using Sentinel.Runtime.Application.Abstractions.Providers;
using Sentinel.Runtime.Application.Incidents.InProcess;
using Sentinel.Runtime.Application.Options;

namespace Sentinel.Runtime.Tests;

internal sealed class ConcurrentOutbox : IOutbox
{
    private readonly ConcurrentQueue<object> _messages = new();

    public IReadOnlyList<object> Messages => [.. _messages];

    public void Enqueue<TMessage>(TMessage message, MessageMetadata? metadata = null)
        where TMessage : class => _messages.Enqueue(message);

    public IEnumerable<TMessage> Of<TMessage>() => _messages.OfType<TMessage>();
}
