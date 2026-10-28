using CleanArchitecture.BuildingBlocks;
using Shouldly;

namespace CleanArchitecture.BuildingBlocks.Tests;

public sealed class ResultTests
{
    [Fact]
    public void Success_result_should_be_successful()
    {
        var result = Result.Success();

        result.IsSuccess.ShouldBeTrue();
        result.IsFailure.ShouldBeFalse();
    }

    [Fact]
    public void Failure_result_should_not_be_successful()
    {
        var result = Result.Failure(Error.Failure("test", "Test error"));

        result.IsSuccess.ShouldBeFalse();
        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("test");
    }

    [Fact]
    public void Generic_success_should_carry_value()
    {
        var result = Result.Success(42);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(42);
    }

    [Fact]
    public void Generic_failure_should_carry_error()
    {
        var result = Result.Failure<int>(Error.NotFound("item", "Not found"));

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("item");
    }
}
