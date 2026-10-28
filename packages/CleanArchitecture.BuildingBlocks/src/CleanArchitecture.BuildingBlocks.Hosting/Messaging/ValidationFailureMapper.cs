using FluentValidation.Results;

namespace CleanArchitecture.BuildingBlocks.Messaging;

public static class ValidationFailureMapper
{
    public static ValidationError ToValidationError(ValidationResult result) =>
        new(result.Errors
            .Select(f => Error.Problem(f.ErrorCode, f.ErrorMessage))
            .ToArray());
}
