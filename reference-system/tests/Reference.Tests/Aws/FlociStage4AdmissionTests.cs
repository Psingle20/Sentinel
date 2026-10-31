using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Amazon.IdentityManagement;
using Amazon.IdentityManagement.Model;
using Amazon.Runtime;
using Amazon.SecurityToken;
using Amazon.SecurityToken.Model;
using Amazon.SQS;
using Amazon.SQS.Model;
using Shouldly;
using Xunit.Abstractions;

namespace Reference.Tests.Aws;

/// <summary>
/// The admission checks of Phase E01's Stage 4 against local AWS: what an EventBridge route-drift scenario
/// (<c>AWS-S7</c>) and an IAM permission-drift scenario (<c>AWS-S4</c>) would rely on. EventBridge routes a matching event
/// to an SQS target, a changed pattern stops it, and the rule reports its pattern and targets, so <c>AWS-S7</c> can be
/// admitted. IAM stores and versions policies, and restores a default version, but enforces none of them, so a
/// permission removed from a role changes nothing a workload does and <c>AWS-S4</c> is not admitted (ADR-021). No
/// EventBridge client is pinned yet, so its calls are made over the service's JSON protocol, which local AWS accepts
/// unsigned.
/// </summary>
[Collection(LocalAwsCollection.Name)]
[Trait("Category", "Aws")]
public sealed class FlociStage4AdmissionTests(LocalAws localAws, ITestOutputHelper output) : IClassFixture<LocalAws>
{
    [Fact]
    public async Task An_eventbridge_rule_routes_matching_events_to_its_queue_until_its_pattern_changes()
    {
        using AmazonSQSClient sqs = localAws.Sqs();
        string queue = (await sqs.CreateQueueAsync(new CreateQueueRequest { QueueName = $"routed-{Guid.NewGuid():N}"[..20] })).QueueUrl;
        string queueArn = (await sqs.GetQueueAttributesAsync(new GetQueueAttributesRequest { QueueUrl = queue, AttributeNames = ["QueueArn"] })).Attributes["QueueArn"];
        string rule = $"orders-{Guid.NewGuid():N}"[..20];
        using var http = new HttpClient { BaseAddress = localAws.Endpoint };

        await EventsAsync(http, "PutRule", new { Name = rule, EventPattern = """{"source":["reference.checkout"]}""", State = "ENABLED" });
        await EventsAsync(http, "PutTargets", new { Rule = rule, Targets = new[] { new { Id = "orders", Arn = queueArn } } });
        await EventsAsync(http, "PutEvents", new { Entries = new[] { new { Source = "reference.checkout", DetailType = "OrderPlaced", Detail = """{"orderId":"o1"}""" } } });
        Message routed = (await sqs.ReceiveMessageAsync(new ReceiveMessageRequest { QueueUrl = queue, WaitTimeSeconds = 5, MaxNumberOfMessages = 1 })).Messages.ShouldNotBeNull().ShouldHaveSingleItem("a matching event reaches the rule's queue");
        JsonDocument.Parse(routed.Body).RootElement.GetProperty("detail").GetProperty("orderId").GetString().ShouldBe("o1");
        await sqs.DeleteMessageAsync(queue, routed.ReceiptHandle);

        await EventsAsync(http, "PutRule", new { Name = rule, EventPattern = """{"source":["reference.checkout-v2"]}""", State = "ENABLED" });
        await EventsAsync(http, "PutEvents", new { Entries = new[] { new { Source = "reference.checkout", DetailType = "OrderPlaced", Detail = """{"orderId":"o2"}""" } } });
        ((await sqs.ReceiveMessageAsync(new ReceiveMessageRequest { QueueUrl = queue, WaitTimeSeconds = 5, MaxNumberOfMessages = 1 })).Messages ?? []).ShouldBeEmpty("the drifted pattern no longer matches");

        using JsonDocument described = await EventsAsync(http, "DescribeRule", new { Name = rule });
        described.RootElement.GetProperty("EventPattern").GetString().ShouldBe("""{"source":["reference.checkout-v2"]}""", "the rule reports the pattern it now has");
        using JsonDocument targets = await EventsAsync(http, "ListTargetsByRule", new { Rule = rule });
        targets.RootElement.GetProperty("Targets")[0].GetProperty("Arn").GetString().ShouldBe(queueArn);
    }

    [Fact]
    public async Task Iam_versions_policies_and_restores_a_default_but_enforces_none_of_them()
    {
        const string account = "333333333333";
        using AmazonIdentityManagementServiceClient iam = localAws.Iam(account);
        using AmazonSecurityTokenServiceClient sts = localAws.Sts(account);
        string trust = JsonSerializer.Serialize(new { Version = "2012-10-17", Statement = new[] { new { Effect = "Allow", Principal = new { AWS = $"arn:aws:iam::{account}:root" }, Action = "sts:AssumeRole" } } });
        string name = $"consumer-{Guid.NewGuid():N}"[..20];
        string role = (await iam.CreateRoleAsync(new CreateRoleRequest { RoleName = name, AssumeRolePolicyDocument = trust })).Role.Arn;
        await iam.PutRolePolicyAsync(new PutRolePolicyRequest
        {
            RoleName = name,
            PolicyName = "deny-sqs",
            PolicyDocument = JsonSerializer.Serialize(new { Version = "2012-10-17", Statement = new[] { new { Effect = "Deny", Action = "sqs:*", Resource = "*" } } })
        });
        Credentials assumed = (await sts.AssumeRoleAsync(new AssumeRoleRequest { RoleArn = role, RoleSessionName = "consumer" })).Credentials;
        using var asRole = new AmazonSQSClient(
            new SessionAWSCredentials(assumed.AccessKeyId, assumed.SecretAccessKey, assumed.SessionToken),
            new AmazonSQSConfig { ServiceURL = localAws.Endpoint.ToString(), AuthenticationRegion = LocalAws.Region });

        await Should.NotThrowAsync(() => asRole.ListQueuesAsync(new ListQueuesRequest()), "local AWS does not enforce IAM: a role denied sqs:* still calls SQS");

        string policy = (await iam.CreatePolicyAsync(new CreatePolicyRequest { PolicyName = $"orders-{Guid.NewGuid():N}"[..20], PolicyDocument = Allow("sqs:ReceiveMessage", "sqs:DeleteMessage") })).Policy.Arn;
        await iam.CreatePolicyVersionAsync(new CreatePolicyVersionRequest { PolicyArn = policy, PolicyDocument = Allow("sqs:ReceiveMessage"), SetAsDefault = true });
        ListPolicyVersionsResponse versions = await iam.ListPolicyVersionsAsync(new ListPolicyVersionsRequest { PolicyArn = policy });
        versions.Versions.ShouldNotBeNull().Select(version => (version.VersionId, version.IsDefaultVersion)).ShouldBe([("v1", false), ("v2", true)], ignoreOrder: true);
        await iam.SetDefaultPolicyVersionAsync(new SetDefaultPolicyVersionRequest { PolicyArn = policy, VersionId = "v1" });
        (await iam.GetPolicyAsync(new GetPolicyRequest { PolicyArn = policy })).Policy.DefaultVersionId.ShouldBe("v1", "an approved version can be restored");
        output.WriteLine("IAM stores, versions and restores policies, and enforces none.");
    }

    private static string Allow(params string[] actions) =>
        JsonSerializer.Serialize(new { Version = "2012-10-17", Statement = new[] { new { Effect = "Allow", Action = actions, Resource = "*" } } });

    // One EventBridge operation over its JSON protocol; local AWS reads the account from the credential scope.
    private static async Task<JsonDocument> EventsAsync(HttpClient http, string operation, object body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/") { Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8) };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/x-amz-json-1.1");
        request.Headers.Add("X-Amz-Target", $"AWSEvents.{operation}");
        request.Headers.Add("X-Amz-Date", DateTime.UtcNow.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture));
        request.Headers.TryAddWithoutValidation(
            "Authorization",
            string.Create(CultureInfo.InvariantCulture, $"AWS4-HMAC-SHA256 Credential={LocalAws.DefaultAccount}/{DateTime.UtcNow:yyyyMMdd}/{LocalAws.Region}/events/aws4_request, SignedHeaders=host;x-amz-date, Signature=0"));
        using HttpResponseMessage response = await http.SendAsync(request);
        string text = await response.Content.ReadAsStringAsync();
        response.IsSuccessStatusCode.ShouldBeTrue($"{operation} answered {(int)response.StatusCode}: {text}");
        return JsonDocument.Parse(text.Length == 0 ? "{}" : text);
    }
}
