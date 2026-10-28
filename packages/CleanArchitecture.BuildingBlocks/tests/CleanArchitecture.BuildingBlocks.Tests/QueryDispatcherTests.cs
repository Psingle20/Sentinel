using CleanArchitecture.BuildingBlocks.Dispatchers;
using CleanArchitecture.BuildingBlocks.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace CleanArchitecture.BuildingBlocks.Tests;

public sealed class QueryDispatcherTests
{
    private sealed record TestQuery(int Id) : IQuery<string>;

    private sealed class TestQueryHandler : IQueryHandler<TestQuery, string>
    {
        public Task<Result<string>> Handle(TestQuery query, CancellationToken cancellationToken)
        {
            return Task.FromResult(Result.Success($"Item-{query.Id}"));
        }
    }

    [Fact]
    public async Task Should_dispatch_query()
    {
        ServiceCollection services = new();
        services.AddScoped<IQueryHandler<TestQuery, string>, TestQueryHandler>();
        ServiceProvider provider = services.BuildServiceProvider();

        QueryDispatcher dispatcher = new(provider);

        Result<string> result = await dispatcher.Dispatch(new TestQuery(42), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe("Item-42");
    }
}
