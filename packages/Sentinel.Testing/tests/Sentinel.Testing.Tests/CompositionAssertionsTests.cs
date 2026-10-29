using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Infrastructure.Messaging;
using CleanArchitecture.BuildingBlocks.Messaging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Sentinel.Testing.Composition;
using Shouldly;

namespace Sentinel.Testing.Tests;

/// <summary>The composition harness catches each rule it is named for.</summary>
public sealed class CompositionAssertionsTests
{
    public sealed record ApprovePlanRequest(int Version) : IRequest<string>;

    public sealed class ApprovePlanProcessor : IProcessor<ApprovePlanRequest, string>
    {
        public Task<Result<string>> Process(ApprovePlanRequest request, CancellationToken cancellationToken) => Task.FromResult(Result.Success("approved"));
    }

    public sealed class SecondApprovePlanProcessor : IProcessor<ApprovePlanRequest, string>
    {
        public Task<Result<string>> Process(ApprovePlanRequest request, CancellationToken cancellationToken) => Task.FromResult(Result.Success("again"));
    }

    public interface IApprovalStore;

    public sealed class ScopedStore;

    public sealed class SingletonCapturingScoped(ScopedStore store)
    {
        public ScopedStore Store { get; } = store;
    }

    public sealed record PlanApproved(int Version);

    public sealed class PlanApprovedConsumer : IIntegrationMessageHandler<PlanApproved>
    {
        public Task<Result> HandleAsync(PlanApproved message, MessageEnvelope envelope, CancellationToken cancellationToken) => Task.FromResult(Result.Success());
    }

    public sealed class Worker : BackgroundService
    {
        protected override Task ExecuteAsync(CancellationToken stoppingToken) => Task.CompletedTask;
    }

    // Stands in for an Infrastructure module that also registers an Application service.
    public sealed class LeakyInfrastructureModule : IDependencyModule
    {
        public void RegisterServices(IServiceCollection services, IConfiguration configuration) =>
            services.AddScoped(_ => new ApprovePlanProcessor());
    }

    private static readonly System.Reflection.Assembly ThisAssembly = typeof(CompositionAssertionsTests).Assembly;

    [Fact]
    public void A_request_without_a_processor_or_with_two_is_reported()
    {
        var none = new ServiceCollection();
        var two = new ServiceCollection()
            .AddScoped<IProcessor<ApprovePlanRequest, string>, ApprovePlanProcessor>()
            .AddScoped<IProcessor<ApprovePlanRequest, string>, SecondApprovePlanProcessor>();

        CompositionAssertions.HandlerProblems(none, none.BuildServiceProvider(), ThisAssembly).ShouldContain(problem => problem.Contains("0 registrations", StringComparison.Ordinal));
        CompositionAssertions.HandlerProblems(two, two.BuildServiceProvider(), ThisAssembly).ShouldContain(problem => problem.Contains("2 registrations", StringComparison.Ordinal));
    }

    [Fact]
    public void A_request_with_exactly_one_processor_passes()
    {
        var one = new ServiceCollection().AddScoped<IProcessor<ApprovePlanRequest, string>, ApprovePlanProcessor>();

        CompositionAssertions.HandlerProblems(one, one.BuildServiceProvider(), ThisAssembly).ShouldBeEmpty();
    }

    [Fact]
    public void A_port_without_an_implementation_is_reported_unless_it_is_a_collection()
    {
        var services = new ServiceCollection();

        CompositionAssertions.PortProblems(services, [], ThisAssembly).ShouldContain(problem => problem.Contains(nameof(IApprovalStore), StringComparison.Ordinal));
    }

    [Fact]
    public void A_singleton_that_captures_a_scoped_service_is_reported()
    {
        var services = new ServiceCollection().AddScoped<ScopedStore>().AddSingleton<SingletonCapturingScoped>();

        CompositionAssertions.LifetimeProblems(services).ShouldHaveSingleItem().ShouldContain(nameof(ScopedStore));
    }

    [Fact]
    public void A_hosted_service_or_consumer_registered_twice_is_reported()
    {
        var services = new ServiceCollection()
            .AddScoped<IIntegrationMessageHandler<PlanApproved>, PlanApprovedConsumer>()
            .AddScoped<IIntegrationMessageHandler<PlanApproved>, PlanApprovedConsumer>();
        services.AddSingleton<IHostedService, Worker>();
        services.AddSingleton<IHostedService, Worker>();

        IReadOnlyList<string> problems = CompositionAssertions.DuplicateProblems(services);

        problems.ShouldContain(problem => problem.Contains(nameof(Worker), StringComparison.Ordinal));
        problems.ShouldContain(problem => problem.Contains(nameof(PlanApproved), StringComparison.Ordinal));
    }

    [Fact]
    public void A_module_that_registers_a_type_outside_its_layer_is_caught()
    {
        IReadOnlyList<string> problems = CompositionAssertions.ModuleProblems(new LeakyInfrastructureModule(), new ConfigurationBuilder().Build(), [], ThisAssembly);

        problems.ShouldHaveSingleItem().ShouldContain(nameof(ApprovePlanProcessor));
    }
}
