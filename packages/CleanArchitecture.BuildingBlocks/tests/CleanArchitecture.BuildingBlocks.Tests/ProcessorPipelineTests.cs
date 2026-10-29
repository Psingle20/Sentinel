using CleanArchitecture.BuildingBlocks.Messaging;
using CleanArchitecture.BuildingBlocks.Tests.TestSupport;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shouldly;

namespace CleanArchitecture.BuildingBlocks.Tests;

/// <summary>The inbound seam (ADR-020): request, validation, processor, one command or query, its handler.</summary>
public sealed class ProcessorPipelineTests
{
    internal sealed class CallLog
    {
        public List<string> Calls { get; } = [];

        public CancellationToken ValidatorToken { get; set; }
    }

    internal sealed record RenameRequest(string Name) : IRequest<string>;

    internal sealed class RenameRequestValidator : AbstractValidator<RenameRequest>
    {
        public RenameRequestValidator(CallLog log)
        {
            RuleFor(request => request.Name).NotEmpty().WithErrorCode("Name.Empty");
            RuleFor(request => request).CustomAsync((_, _, cancellationToken) =>
            {
                log.ValidatorToken = cancellationToken;
                return Task.CompletedTask;
            });
        }
    }

    internal sealed class RenameProcessor(CallLog log, ICommandDispatcher commands) : IProcessor<RenameRequest, string>
    {
        public Task<Result<string>> Process(RenameRequest request, CancellationToken cancellationToken)
        {
            log.Calls.Add("processor");
            return commands.Dispatch(new RenameCommand(request.Name), cancellationToken);
        }
    }

    internal sealed class SecondRenameProcessor(ICommandDispatcher commands) : IProcessor<RenameRequest, string>
    {
        public Task<Result<string>> Process(RenameRequest request, CancellationToken cancellationToken) =>
            commands.Dispatch(new RenameCommand(request.Name), cancellationToken);
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

    internal sealed record ArchiveRequest(string Reason) : IRequest;

    internal sealed class ArchiveRequestValidator : AbstractValidator<ArchiveRequest>
    {
        public ArchiveRequestValidator() => RuleFor(request => request.Reason).NotEmpty();
    }

    internal sealed class ArchiveProcessor(CallLog log) : IProcessor<ArchiveRequest>
    {
        public Task<Result> Process(ArchiveRequest request, CancellationToken cancellationToken)
        {
            log.Calls.Add("archive");
            return Task.FromResult(Result.Success());
        }
    }

    internal sealed record LookupRequest(int Id) : IRequest<string>;

    internal sealed class LookupProcessor(IQueryDispatcher queries) : IProcessor<LookupRequest, string>
    {
        public Task<Result<string>> Process(LookupRequest request, CancellationToken cancellationToken) =>
            queries.Dispatch(new LookupQuery(request.Id), cancellationToken);
    }

    internal sealed record LookupQuery(int Id) : IQuery<string>;

    internal sealed class LookupHandler : IQueryHandler<LookupQuery, string>
    {
        public Task<Result<string>> Handle(LookupQuery query, CancellationToken cancellationToken) =>
            Task.FromResult(Result.Success($"Item-{query.Id}"));
    }

    private static readonly Type[] PipelineTypes =
    [
        typeof(RenameRequestValidator), typeof(RenameProcessor), typeof(RenameHandler),
        typeof(ArchiveRequestValidator), typeof(ArchiveProcessor),
        typeof(LookupProcessor), typeof(LookupHandler)
    ];

    [Fact]
    public async Task A_valid_request_is_mapped_to_one_command_and_reaches_its_handler()
    {
        var (provider, sink, log) = Build(PipelineTypes);
        await using AsyncServiceScope scope = provider.CreateAsyncScope();

        Result<string> result = await scope.ServiceProvider.GetRequiredService<IRequestDispatcher>().HandleRequestAsync(new RenameRequest("checkout"), CancellationToken.None);

        result.Value.ShouldBe("checkout");
        log.Calls.ShouldBe(["processor", "handler"]);
        sink.Entries.Select(entry => entry.Message).ShouldBe(["Processing command RenameCommand", "Completed command RenameCommand"]);
    }

    [Fact]
    public async Task An_invalid_request_is_refused_before_the_processor_and_nothing_is_dispatched()
    {
        var (provider, sink, log) = Build(PipelineTypes);
        await using AsyncServiceScope scope = provider.CreateAsyncScope();

        Result<string> result = await scope.ServiceProvider.GetRequiredService<IRequestDispatcher>().HandleRequestAsync(new RenameRequest(string.Empty), CancellationToken.None);

        result.Error.ShouldBeOfType<ValidationError>().Errors.Select(error => error.Code).ShouldBe(["Name.Empty"]);
        log.Calls.ShouldBeEmpty();
        sink.Entries.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_request_without_a_result_is_validated_the_same_way()
    {
        var (provider, _, log) = Build(PipelineTypes);
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        IRequestDispatcher requests = scope.ServiceProvider.GetRequiredService<IRequestDispatcher>();

        Result refused = await requests.HandleRequestAsync(new ArchiveRequest(string.Empty), CancellationToken.None);
        Result accepted = await requests.HandleRequestAsync(new ArchiveRequest("superseded"), CancellationToken.None);

        refused.Error.ShouldBeOfType<ValidationError>();
        accepted.IsSuccess.ShouldBeTrue();
        log.Calls.ShouldBe(["archive"]);
    }

    [Fact]
    public async Task A_request_without_validators_goes_straight_to_its_processor_and_query()
    {
        var (provider, sink, _) = Build(PipelineTypes);
        await using AsyncServiceScope scope = provider.CreateAsyncScope();

        Result<string> result = await scope.ServiceProvider.GetRequiredService<IRequestDispatcher>().HandleRequestAsync(new LookupRequest(7), CancellationToken.None);

        result.Value.ShouldBe("Item-7");
        sink.Entries.Select(entry => entry.Message).ShouldBe(["Processing query LookupQuery", "Completed query LookupQuery"]);
    }

    [Fact]
    public async Task Request_validators_receive_the_callers_cancellation_token()
    {
        var (provider, _, log) = Build(PipelineTypes);
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        using CancellationTokenSource cancellation = new();

        await scope.ServiceProvider.GetRequiredService<IRequestDispatcher>().HandleRequestAsync(new RenameRequest("checkout"), cancellation.Token);

        log.ValidatorToken.ShouldBe(cancellation.Token);
    }

    [Fact]
    public void Two_processors_for_one_request_are_rejected()
    {
        ServiceCollection services = new();

        Should.Throw<InvalidOperationException>(() => services.AddBuildingBlocks([typeof(RenameProcessor), typeof(SecondRenameProcessor)]))
            .Message.ShouldContain("each request exactly one processor");
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

        ServiceProvider provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        return (provider, sink, log);
    }
}
