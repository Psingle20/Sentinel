using CleanArchitecture.BuildingBlocks.Dispatchers;
using CleanArchitecture.BuildingBlocks.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace CleanArchitecture.BuildingBlocks.Tests;

public sealed class DispatcherBehaviorTests
{
    private sealed record UnhandledCommand : ICommand<string>;

    private sealed record UnhandledQuery : IQuery<string>;

    private sealed record ExplodingCommand : ICommand<string>;

    private sealed class ExplodingHandler : ICommandHandler<ExplodingCommand, string>
    {
        public Task<Result<string>> Handle(ExplodingCommand command, CancellationToken cancellationToken) =>
            throw new InvalidTimeZoneException("handler failed synchronously");
    }

    private sealed record EchoCommand(int Value) : ICommand<int>;

    private sealed class EchoHandler : ICommandHandler<EchoCommand, int>
    {
        public async Task<Result<int>> Handle(EchoCommand command, CancellationToken cancellationToken)
        {
            await Task.Yield();
            return Result.Success(command.Value);
        }
    }

    [Fact]
    public async Task Missing_command_handler_fails_with_the_command_type()
    {
        CommandDispatcher dispatcher = new(new ServiceCollection().BuildServiceProvider());

        var exception = await Should.ThrowAsync<InvalidOperationException>(
            async () => await dispatcher.Dispatch(new UnhandledCommand(), CancellationToken.None));

        exception.Message.ShouldContain(nameof(UnhandledCommand));
    }

    [Fact]
    public async Task Missing_query_handler_fails_with_the_query_type()
    {
        QueryDispatcher dispatcher = new(new ServiceCollection().BuildServiceProvider());

        var exception = await Should.ThrowAsync<InvalidOperationException>(
            async () => await dispatcher.Dispatch(new UnhandledQuery(), CancellationToken.None));

        exception.Message.ShouldContain(nameof(UnhandledQuery));
    }

    [Fact]
    public async Task Handler_exceptions_are_not_wrapped_by_reflection()
    {
        ServiceCollection services = new();
        services.AddScoped<ICommandHandler<ExplodingCommand, string>, ExplodingHandler>();
        CommandDispatcher dispatcher = new(services.BuildServiceProvider());

        await Should.ThrowAsync<InvalidTimeZoneException>(
            async () => await dispatcher.Dispatch(new ExplodingCommand(), CancellationToken.None));
    }

    [Fact]
    public async Task Concurrent_dispatches_resolve_their_own_handlers_and_results()
    {
        ServiceCollection services = new();
        services.AddBuildingBlocks([typeof(EchoHandler)]);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        ServiceProvider provider = services.BuildServiceProvider();

        int[] results = await Task.WhenAll(Enumerable.Range(0, 200).Select(async value =>
        {
            await using var scope = provider.CreateAsyncScope();
            var dispatcher = scope.ServiceProvider.GetRequiredService<ICommandDispatcher>();
            Result<int> result = await dispatcher.Dispatch(new EchoCommand(value), CancellationToken.None);
            return result.Value;
        }));

        results.ShouldBe(Enumerable.Range(0, 200).ToArray());
    }
}
