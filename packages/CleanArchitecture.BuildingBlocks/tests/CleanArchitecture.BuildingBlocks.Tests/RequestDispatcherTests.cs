using CleanArchitecture.BuildingBlocks.Dispatchers;
using CleanArchitecture.BuildingBlocks.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace CleanArchitecture.BuildingBlocks.Tests;

public sealed class RequestDispatcherTests
{
    private sealed record ShoutRequest(string Value) : IRequest<string>;

    private sealed class ShoutProcessor : IProcessor<ShoutRequest, string>
    {
        public CancellationToken Token { get; private set; }

        public Task<Result<string>> Process(ShoutRequest request, CancellationToken cancellationToken)
        {
            Token = cancellationToken;
            return Task.FromResult(Result.Success(request.Value.ToUpperInvariant()));
        }
    }

    private sealed record ForgetRequest(string Value) : IRequest;

    private sealed class ForgetProcessor : IProcessor<ForgetRequest>
    {
        public Task<Result> Process(ForgetRequest request, CancellationToken cancellationToken) => Task.FromResult(Result.Success());
    }

    private sealed record FaultingRequest : IRequest<string>;

    private sealed class FaultingProcessor : IProcessor<FaultingRequest, string>
    {
        public Task<Result<string>> Process(FaultingRequest request, CancellationToken cancellationToken) =>
            throw new TimeoutException("The processor's own failure.");
    }

    private sealed record UnhandledRequest : IRequest;

    [Fact]
    public async Task A_request_with_a_result_reaches_its_processor()
    {
        var processor = new ShoutProcessor();
        ServiceProvider provider = new ServiceCollection().AddSingleton<IProcessor<ShoutRequest, string>>(processor).BuildServiceProvider();
        using CancellationTokenSource cancellation = new();

        Result<string> result = await new RequestDispatcher(provider).SendAsync(new ShoutRequest("approve"), cancellation.Token);

        result.Value.ShouldBe("APPROVE");
        processor.Token.ShouldBe(cancellation.Token);
    }

    [Fact]
    public async Task A_request_without_a_result_reaches_its_processor()
    {
        ServiceProvider provider = new ServiceCollection().AddScoped<IProcessor<ForgetRequest>, ForgetProcessor>().BuildServiceProvider();

        Result result = await new RequestDispatcher(provider).SendAsync(new ForgetRequest("x"), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task A_request_without_a_processor_fails_naming_the_request()
    {
        ServiceProvider provider = new ServiceCollection().BuildServiceProvider();

        InvalidOperationException missing = await Should.ThrowAsync<InvalidOperationException>(
            () => new RequestDispatcher(provider).SendAsync(new UnhandledRequest(), CancellationToken.None));

        missing.Message.ShouldContain(nameof(UnhandledRequest));
    }

    [Fact]
    public async Task A_faulting_processor_surfaces_its_own_exception()
    {
        ServiceProvider provider = new ServiceCollection().AddScoped<IProcessor<FaultingRequest, string>, FaultingProcessor>().BuildServiceProvider();

        TimeoutException fault = await Should.ThrowAsync<TimeoutException>(
            () => new RequestDispatcher(provider).SendAsync(new FaultingRequest(), CancellationToken.None));

        fault.Message.ShouldBe("The processor's own failure.");
    }
}
