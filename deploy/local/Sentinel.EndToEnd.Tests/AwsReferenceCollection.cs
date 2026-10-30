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

[CollectionDefinition(Name)]
public sealed class AwsReferenceCollection : ICollectionFixture<AwsReferenceOnlySystem>
{
    public const string Name = "aws-reference";
}
