using CleanArchitecture.BuildingBlocks.Behaviors;
using CleanArchitecture.BuildingBlocks.Messaging;
using FluentValidation;
using Shouldly;

namespace CleanArchitecture.BuildingBlocks.Tests;

public sealed class ValidationDecoratorTests
{
    private sealed record TestCommand(string Name) : ICommand<string>;

    private sealed class TestCommandHandler : ICommandHandler<TestCommand, string>
    {
        public Task<Result<string>> Handle(TestCommand command, CancellationToken cancellationToken)
        {
            return Task.FromResult(Result.Success(command.Name));
        }
    }

    private sealed class TestCommandValidator : AbstractValidator<TestCommand>
    {
        public TestCommandValidator()
        {
            RuleFor(x => x.Name).NotEmpty().WithErrorCode("Name.Empty");
        }
    }

    [Fact]
    public async Task Should_pass_through_when_valid()
    {
        TestCommandHandler inner = new();
        IValidator<TestCommand>[] validators = [new TestCommandValidator()];

        ValidationDecorator.CommandHandler<TestCommand, string> decorator = new(inner, validators);

        Result<string> result = await decorator.Handle(new TestCommand("Valid"), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe("Valid");
    }

    [Fact]
    public async Task Should_return_validation_error_when_invalid()
    {
        TestCommandHandler inner = new();
        IValidator<TestCommand>[] validators = [new TestCommandValidator()];

        ValidationDecorator.CommandHandler<TestCommand, string> decorator = new(inner, validators);

        Result<string> result = await decorator.Handle(new TestCommand(""), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBeOfType<ValidationError>();
    }
}
