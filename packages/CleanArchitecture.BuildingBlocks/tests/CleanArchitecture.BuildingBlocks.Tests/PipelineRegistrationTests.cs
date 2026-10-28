using CleanArchitecture.BuildingBlocks.Messaging;
using CleanArchitecture.BuildingBlocks.Tests.TestSupport;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shouldly;

namespace CleanArchitecture.BuildingBlocks.Tests;

public sealed class PipelineRegistrationTests
{
    internal sealed class CallLog
    {
        public List<string> Calls { get; } = [];

        public CancellationToken ValidatorToken { get; set; }
    }

    internal sealed record RenameCommand(string Name) : ICommand<string>;

    internal sealed class RenameHandler(CallLog log) : ICommandHandler<RenameCommand, string>
    {
        public Task<Result<string>> Handle(RenameCommand command, CancellationToken cancellationToken)
        {
            log.Calls.Add("handler");
            return Task.FromResult(Result.Success(command.Name));
        }
    }

    internal sealed class SecondRenameHandler : ICommandHandler<RenameCommand, string>
    {
        public Task<Result<string>> Handle(RenameCommand command, CancellationToken cancellationToken) =>
            Task.FromResult(Result.Success(command.Name));
    }

    internal sealed class RenameValidator : AbstractValidator<RenameCommand>
    {
        public RenameValidator(CallLog log)
        {
            RuleFor(command => command.Name).NotEmpty().WithErrorCode("Name.Empty");
            RuleFor(command => command).CustomAsync((_, _, cancellationToken) =>
            {
                log.ValidatorToken = cancellationToken;
                return Task.CompletedTask;
            });
        }
    }

    internal sealed record ArchiveCommand(string Reason) : ICommand;

    internal sealed class ArchiveHandler(CallLog log) : ICommandHandler<ArchiveCommand>
    {
        public Task<Result> Handle(ArchiveCommand command, CancellationToken cancellationToken)
        {
            log.Calls.Add("archive");
            return Task.FromResult(Result.Success());
        }
    }

    internal sealed class ArchiveValidator : AbstractValidator<ArchiveCommand>
    {
        public ArchiveValidator() => RuleFor(command => command.Reason).NotEmpty();
    }

    internal sealed record LookupQuery(int Id) : IQuery<string>;

    internal sealed class LookupHandler : IQueryHandler<LookupQuery, string>
    {
        public Task<Result<string>> Handle(LookupQuery query, CancellationToken cancellationToken) =>
            Task.FromResult(Result.Success($"Item-{query.Id}"));
    }

    private static readonly Type[] PipelineTypes =
        [typeof(RenameHandler), typeof(RenameValidator), typeof(ArchiveHandler), typeof(ArchiveValidator), typeof(LookupHandler)];

    [Fact]
    public async Task Valid_command_passes_validation_and_logging_and_reaches_the_handler()
    {
        var (provider, sink, log) = Build(PipelineTypes);
        await using var scope = provider.CreateAsyncScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<ICommandDispatcher>();

        Result<string> result = await dispatcher.Dispatch(new RenameCommand("checkout"), CancellationToken.None);

        result.Value.ShouldBe("checkout");
        log.Calls.ShouldBe(["handler"]);
        sink.Entries.Select(entry => entry.Message).ShouldBe(
            ["Processing command RenameCommand", "Completed command RenameCommand"]);
    }

    [Fact]
    public async Task Invalid_command_is_rejected_by_validation_running_inside_logging()
    {
        var (provider, sink, log) = Build(PipelineTypes);
        await using var scope = provider.CreateAsyncScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<ICommandDispatcher>();

        Result<string> result = await dispatcher.Dispatch(new RenameCommand(""), CancellationToken.None);

        result.Error.ShouldBeOfType<ValidationError>();
        log.Calls.ShouldBeEmpty();

        LogEntry failure = sink.Entries.Single(entry => entry.Level == LogLevel.Error);
        failure.Message.ShouldBe("Completed command RenameCommand with error");
        failure.Scopes.ShouldHaveSingleItem().ShouldBeAssignableTo<IDictionary<string, object?>>()!["Error"]
            .ShouldBeOfType<ValidationError>();
    }

    [Fact]
    public async Task Commands_without_a_result_use_the_same_pipeline()
    {
        var (provider, sink, log) = Build(PipelineTypes);
        await using var scope = provider.CreateAsyncScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<ICommandDispatcher>();

        Result result = await dispatcher.Dispatch(new ArchiveCommand(""), CancellationToken.None);

        result.Error.ShouldBeOfType<ValidationError>();
        log.Calls.ShouldBeEmpty();
        sink.Entries.ShouldContain(entry => entry.Message == "Completed command ArchiveCommand with error");
    }

    [Fact]
    public async Task Queries_are_logged()
    {
        var (provider, sink, _) = Build(PipelineTypes);
        await using var scope = provider.CreateAsyncScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IQueryDispatcher>();

        Result<string> result = await dispatcher.Dispatch(new LookupQuery(7), CancellationToken.None);

        result.Value.ShouldBe("Item-7");
        sink.Entries.Select(entry => entry.Message).ShouldBe(
            ["Processing query LookupQuery", "Completed query LookupQuery"]);
    }

    [Fact]
    public async Task Validators_receive_the_callers_cancellation_token()
    {
        var (provider, _, log) = Build(PipelineTypes);
        await using var scope = provider.CreateAsyncScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<ICommandDispatcher>();
        using CancellationTokenSource cancellation = new();

        await dispatcher.Dispatch(new RenameCommand("checkout"), cancellation.Token);

        log.ValidatorToken.ShouldBe(cancellation.Token);
    }

    [Fact]
    public void Registering_twice_is_rejected()
    {
        ServiceCollection services = new();
        services.AddBuildingBlocks(PipelineTypes);

        Should.Throw<InvalidOperationException>(() => services.AddBuildingBlocks(PipelineTypes))
            .Message.ShouldContain("already been called");
    }

    [Fact]
    public void Two_handlers_for_one_command_are_rejected()
    {
        ServiceCollection services = new();

        Should.Throw<InvalidOperationException>(() =>
                services.AddBuildingBlocks([typeof(RenameHandler), typeof(SecondRenameHandler)]))
            .Message.ShouldContain("exactly one handler");
    }

    private static (ServiceProvider Provider, LogSink Sink, CallLog Log) Build(Type[] types)
    {
        LogSink sink = new();
        CallLog log = new();
        ServiceCollection services = new();
        services.AddSingleton(sink);
        services.AddSingleton(log);
        services.AddSingleton(typeof(ILogger<>), typeof(CapturingLogger<>));
        services.AddBuildingBlocks(types);

        ServiceProvider provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true
        });

        return (provider, sink, log);
    }
}
