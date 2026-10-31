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

/// <summary>The reference system alone with the AWS environment (Phase E01): the queue-consumer on ECS and SQS on local AWS.</summary>
public sealed class AwsReferenceOnlySystem : SentinelSystem
{
    public AwsReferenceOnlySystem()
        : base(referenceOnly: true, referenceEnvironment: "aws")
    {
    }
}
