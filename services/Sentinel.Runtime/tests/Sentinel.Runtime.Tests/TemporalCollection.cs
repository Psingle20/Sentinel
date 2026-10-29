using System.Net;
using System.Net.Sockets;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Networks;
using Temporalio.Client;

namespace Sentinel.Runtime.Tests;

[CollectionDefinition(Name)]
public sealed class TemporalCollection : ICollectionFixture<TemporalFixture>
{
    public const string Name = "temporal";
}
