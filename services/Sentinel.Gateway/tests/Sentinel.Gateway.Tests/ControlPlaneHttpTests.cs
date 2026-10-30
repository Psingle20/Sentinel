using System.Net;
using System.Text;
using Sentinel.Gateway.Application.Abstractions.Providers;
using Sentinel.Gateway.Infrastructure.Clients;
using Shouldly;

namespace Sentinel.Gateway.Tests;

/// <summary>How an owning service's refusal reaches the Gateway: its status, its description and its stable error code.</summary>
public sealed class ControlPlaneHttpTests
{
    [Theory]
    [InlineData(422, """{"title":"Configuration.Invalid","status":422,"detail":"Provider 'meta' is not configured.","code":"Configuration.Invalid"}""", "Configuration.Invalid", "Provider 'meta' is not configured.")]
    [InlineData(409, """{"title":"Configuration.VersionConflict","status":409,"detail":"The configuration changed since it was read."}""", "Configuration.VersionConflict", "The configuration changed since it was read.")]
    [InlineData(422, """{"title":"Unprocessable Entity","status":422,"detail":"The document is empty."}""", null, "The document is empty.")]
    [InlineData(503, "the owner answered in plain text", null, "the owner answered in plain text")]
    public async Task A_refusal_keeps_its_status_description_and_error_code(int status, string body, string? code, string description)
    {
        using var http = new HttpClient(new Answering((HttpStatusCode)status, body)) { BaseAddress = new Uri("http://owner.test") };

        ControlPlaneResponse response = await ControlPlaneHttp.SendAsync(http, HttpMethod.Put, "/admin/configuration", new { }, CancellationToken.None);

        response.StatusCode.ShouldBe(status);
        response.Problem.ShouldBe(description);
        response.Code.ShouldBe(code);
    }

    private sealed class Answering(HttpStatusCode status, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/problem+json") });
    }
}
