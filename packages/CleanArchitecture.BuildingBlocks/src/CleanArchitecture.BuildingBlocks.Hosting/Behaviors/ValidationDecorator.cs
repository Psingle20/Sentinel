using CleanArchitecture.BuildingBlocks.Messaging;
using FluentValidation;
using FluentValidation.Results;

namespace CleanArchitecture.BuildingBlocks.Behaviors;

internal static class ValidationDecorator
{
    internal sealed class CommandHandler<TCommand, TResponse>(
        ICommandHandler<TCommand, TResponse> innerHandler,
        IEnumerable<IValidator<TCommand>> validators)
        : ICommandHandler<TCommand, TResponse>
        where TCommand : ICommand<TResponse>
    {
        public async Task<Result<TResponse>> Handle(TCommand command, CancellationToken cancellationToken)
        {
            ValidationFailure[] validationFailures = await ValidateAsync(command, validators, cancellationToken);

            if (validationFailures.Length == 0)
            {
                return await innerHandler.Handle(command, cancellationToken);
            }

            return Result.Failure<TResponse>(CreateValidationError(validationFailures));
        }
    }

    internal sealed class CommandBaseHandler<TCommand>(
        ICommandHandler<TCommand> innerHandler,
        IEnumerable<IValidator<TCommand>> validators)
        : ICommandHandler<TCommand>
        where TCommand : ICommand
    {
        public async Task<Result> Handle(TCommand command, CancellationToken cancellationToken)
        {
            ValidationFailure[] validationFailures = await ValidateAsync(command, validators, cancellationToken);

            if (validationFailures.Length == 0)
            {
                return await innerHandler.Handle(command, cancellationToken);
            }

            return Result.Failure(CreateValidationError(validationFailures));
        }
    }

    // Request validation (ADR-020): the processor's own code only maps and dispatches.
    internal sealed class Processor<TRequest, TResult>(
        IProcessor<TRequest, TResult> innerProcessor,
        IEnumerable<IValidator<TRequest>> validators)
        : IProcessor<TRequest, TResult>
        where TRequest : IRequest<TResult>
    {
        public async Task<Result<TResult>> Process(TRequest request, CancellationToken cancellationToken)
        {
            ValidationFailure[] validationFailures = await ValidateAsync(request, validators, cancellationToken);

            if (validationFailures.Length == 0)
            {
                return await innerProcessor.Process(request, cancellationToken);
            }

            return Result.Failure<TResult>(CreateValidationError(validationFailures));
        }
    }

    internal sealed class ProcessorBase<TRequest>(
        IProcessor<TRequest> innerProcessor,
        IEnumerable<IValidator<TRequest>> validators)
        : IProcessor<TRequest>
        where TRequest : IRequest
    {
        public async Task<Result> Process(TRequest request, CancellationToken cancellationToken)
        {
            ValidationFailure[] validationFailures = await ValidateAsync(request, validators, cancellationToken);

            if (validationFailures.Length == 0)
            {
                return await innerProcessor.Process(request, cancellationToken);
            }

            return Result.Failure(CreateValidationError(validationFailures));
        }
    }

    private static async Task<ValidationFailure[]> ValidateAsync<TCommand>(
        TCommand command,
        IEnumerable<IValidator<TCommand>> validators,
        CancellationToken cancellationToken)
    {
        if (!validators.Any())
        {
            return [];
        }

        var context = new ValidationContext<TCommand>(command);

        ValidationResult[] validationResults = await Task.WhenAll(
            validators.Select(validator => validator.ValidateAsync(context, cancellationToken)));

        ValidationFailure[] validationFailures = validationResults
            .Where(validationResult => !validationResult.IsValid)
            .SelectMany(validationResult => validationResult.Errors)
            .ToArray();

        return validationFailures;
    }

    private static ValidationError CreateValidationError(ValidationFailure[] validationFailures) =>
        new(validationFailures.Select(f => Error.Problem(f.ErrorCode, f.ErrorMessage)).ToArray());
}
