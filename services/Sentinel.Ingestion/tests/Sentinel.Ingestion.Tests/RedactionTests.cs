using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Sentinel.Contracts;
using Sentinel.Contracts.Observations;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Contracts.Queries;
using Sentinel.Ingestion.Domain.Observations;
using Sentinel.Ingestion.Infrastructure.Providers;
using Shouldly;
using Sentinel.Ingestion.Application.Abstractions.Providers;
using Sentinel.Ingestion.Application.Options;

namespace Sentinel.Ingestion.Tests;

public sealed class RedactionTests
{
    [Theory]
    [InlineData("login failed for alice@example.test", "login failed for [redacted:email]")]
    [InlineData("Authorization: Bearer abcdefghijklmnop", "Authorization: Bearer [redacted:credential]")]
    [InlineData("retry with password=hunter2 now", "retry with password=[redacted:secret] now")]
    [InlineData("client 10.1.2.3 reset", "client [redacted:ip] reset")]
    [InlineData("token eyJhbGciOiJFUzI1NiJ9.eyJzdWIiOiJ4eXoifQ.c2lnbmF0dXJlLXZhbHVl expired", "token [redacted:token] expired")]
    public void Sensitive_values_are_redacted_before_storage(string text, string expected)
    {
        (string redacted, bool changed) = RedactionPolicy.Apply(text);

        redacted.ShouldBe(expected);
        changed.ShouldBeTrue();
    }

    [Fact]
    public void Ordinary_text_is_left_alone()
    {
        RedactionPolicy.Apply("Checkout cart-42 failed on Redis: max number of clients reached").Redacted.ShouldBeFalse();
    }

    [Fact]
    public void Lines_that_differ_only_in_values_share_a_signature()
    {
        LogSignature.Of("Checkout cart-1042 failed after 3 attempts")
            .ShouldBe(LogSignature.Of("Checkout cart-77 failed after 4 attempts"));
        LogSignature.Of("order 0192f0c4-0000-7000-8000-000000000001 placed")
            .ShouldBe("order <id> placed");
    }
}
