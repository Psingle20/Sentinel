using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Reference.Common.Redis;
using Reference.Common.Revisions;
using Shouldly;
using Testcontainers.Redis;
using Reference.Checkout.Redis;
using Reference.Checkout.Revisions;
using Reference.ReportingWorker.Batch;
using Reference.ReportingWorker.Revisions;
using Reference.RedisExporter.Sampling;

namespace Reference.Tests;

[CollectionDefinition(Name)]
public sealed class RedisCollection : ICollectionFixture<SmallRedis>
{
    public const string Name = "redis";
}
