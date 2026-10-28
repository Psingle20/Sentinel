using CleanArchitecture.BuildingBlocks.Dispatchers;
using CleanArchitecture.BuildingBlocks.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace CleanArchitecture.BuildingBlocks.Tests;

public sealed class CommandDispatcherTests
{
    private sealed record TestCommand(string Value) : ICommand<string>;

    private sealed class TestCommandHandler : ICommandHandler<TestCommand, string>
    {
        public Task<Result<string>> Handle(TestCommand command, CancellationToken cancellationToken)
        {
            return Task.FromResult(Result.Success(command.Value.ToUpperInvariant()));
        }
    }

    private sealed record TestVoidCommand(string Value) : ICommand;

    private sealed class TestVoidCommandHandler : ICommandHandler<TestVoidCommand>
    {
        public Task<Result> Handle(TestVoidCommand command, CancellationToken cancellationToken)
        {
            return Task.FromResult(Result.Success());
        }
    }

    [Fact]
    public async Task Should_dispatch_command_with_result()
    {
        ServiceCollection services = new();
        services.AddScoped<ICommandHandler<TestCommand, string>, TestCommandHandler>();
        ServiceProvider provider = services.BuildServiceProvider();

        CommandDispatcher dispatcher = new(provider);

        Result<string> result = await dispatcher.Dispatch(new TestCommand("hello"), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe("HELLO");
    }

    [Fact]
    public async Task Should_dispatch_void_command()
    {
        ServiceCollection services = new();
        services.AddScoped<ICommandHandler<TestVoidCommand>, TestVoidCommandHandler>();
        ServiceProvider provider = services.BuildServiceProvider();

        CommandDispatcher dispatcher = new(provider);

        Result result = await dispatcher.Dispatch(new TestVoidCommand("test"), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
    }
}
