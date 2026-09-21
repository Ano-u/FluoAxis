namespace FluoAxis.Domain.Tests;

using Xunit;

public class ResultTests
{
    private static readonly AppError BlockingError = new(ErrorCodes.CalibrationNonPositiveMean, ErrorSeverity.Blocking, "mean is not positive");

    [Fact]
    public void Ok_carries_the_value_with_no_errors()
    {
        var result = Result<int>.Ok(42);

        Assert.True(result.IsSuccess);
        Assert.Equal(42, result.Value);
        Assert.Empty(result.Errors);
        Assert.NotEqual(Guid.Empty, result.CorrelationId);
    }

    [Fact]
    public void Fail_carries_errors_and_default_value()
    {
        var result = Result<int>.Fail(BlockingError);

        Assert.False(result.IsSuccess);
        Assert.Equal(0, result.Value);
        Assert.Single(result.Errors);
        Assert.Same(BlockingError, result.Errors[0]);
    }

    [Fact]
    public void Fail_rejects_empty_error_list()
    {
        Assert.Throws<ArgumentException>(() => Result<int>.Fail());
        Assert.Throws<ArgumentException>(() => Result<int>.Fail([]));
    }

    [Fact]
    public void Fail_preserves_multiple_errors_in_order()
    {
        var second = new AppError(ErrorCodes.CalibrationMissingEndpoint, ErrorSeverity.Blocking, "missing endpoint");

        var result = Result<int>.Fail(BlockingError, second);

        Assert.Equal([BlockingError, second], result.Errors);
    }

    [Fact]
    public void Map_transforms_success_values_only()
    {
        var success = Result<int>.Ok(21).Map(x => x * 2);
        Assert.True(success.IsSuccess);
        Assert.Equal(42, success.Value);

        var failure = Result<int>.Fail(BlockingError).Map(x => x * 2);
        Assert.False(failure.IsSuccess);
        Assert.Equal([BlockingError], failure.Errors);
    }

    [Fact]
    public void Bind_chains_successes_and_accumulates_errors()
    {
        var chained = Result<int>.Ok(21).Bind(x => Result<string>.Ok($"value {x}"));
        Assert.True(chained.IsSuccess);
        Assert.Equal("value 21", chained.Value);

        var second = new AppError(ErrorCodes.CalibrationMissingEndpoint, ErrorSeverity.Blocking, "missing endpoint");
        var failed = Result<int>.Fail(BlockingError).Bind(x => Result<string>.Ok($"value {x}"));
        Assert.False(failed.IsSuccess);
        Assert.Equal([BlockingError], failed.Errors);

        var inner = Result<int>.Ok(21).Bind(_ => Result<string>.Fail(second));
        Assert.False(inner.IsSuccess);
        Assert.Equal([second], inner.Errors);
    }

    [Fact]
    public void CorrelationId_is_unique_per_result()
    {
        var first = Result<int>.Ok(1);
        var second = Result<int>.Ok(1);

        Assert.NotEqual(first.CorrelationId, second.CorrelationId);
    }
}
