using Microsoft.Extensions.Configuration;
using Reference.Scenarios.Infrastructure.Environments.Aws;
using Shouldly;

namespace Reference.Tests.Aws;

public sealed class AwsEnvironmentOptionsTests
{
    [Theory]
    [InlineData("http://localhost:4566", "http://host.docker.internal:4566")]
    [InlineData("http://127.0.0.1:9090/api/v1/otlp/v1/metrics", "http://host.docker.internal:9090/api/v1/otlp/v1/metrics")]
    [InlineData("http://jaeger:4318/v1/traces", "http://jaeger:4318/v1/traces")]
    public void An_address_on_this_host_is_rewritten_for_the_containers_the_emulator_starts(string address, string expected)
    {
        AwsEnvironmentOptions.ForContainers(address, "host.docker.internal").ShouldBe(expected);
    }

    [Fact]
    public void The_consumer_task_gets_the_queue_telemetry_and_order_service_as_the_containers_reach_them()
    {
        IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Reference:Aws:ServiceUrl"] = "http://localhost:4566",
            ["Reference:Telemetry:MetricsEndpoint"] = "http://localhost:9090/api/v1/otlp/v1/metrics",
            ["services:order-service:http:0"] = "http://localhost:5103"
        }).Build();

        var options = AwsEnvironmentOptions.From(configuration);

        options.ServiceUrl.ShouldBe(new Uri("http://localhost:4566"));
        options.TaskEnvironment["Reference__Queue__Provider"].ShouldBe("sqs");
        options.TaskEnvironment["Reference__Queue__Sqs__ServiceUrl"].ShouldBe("http://host.docker.internal:4566");
        options.TaskEnvironment["Reference__Queue__Sqs__AccessKey"].ShouldBe("000000000000");
        options.TaskEnvironment["Reference__Telemetry__MetricsEndpoint"].ShouldBe("http://host.docker.internal:9090/api/v1/otlp/v1/metrics");
        options.TaskEnvironment["services__order-service__http__0"].ShouldBe("http://host.docker.internal:5103");
        options.TaskEnvironment.ShouldNotContainKey("Reference__Telemetry__LogsEndpoint");
    }

    [Fact]
    public void Without_the_emulator_endpoint_there_is_no_aws_environment_rather_than_a_fallback_to_aws()
    {
        IConfiguration configuration = new ConfigurationBuilder().Build();

        Should.Throw<InvalidOperationException>(() => AwsEnvironmentOptions.From(configuration)).Message.ShouldContain("ServiceUrl");
    }
}
