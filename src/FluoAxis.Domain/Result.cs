namespace FluoAxis.Domain;

using System.Diagnostics.CodeAnalysis;
/// <summary>
/// Structured operation outcome. Domain and application failures flow
/// through <see cref="Errors"/> instead of exception strings; scientific
/// invalidity is a normal value here, not a thrown exception.
/// </summary>
[SuppressMessage(
    "Design",
    "CA1000:DoNotDeclareStaticMembersOnGenericTypes",
    Justification = "Ok/Fail static factories are the mandated Result<T> contract (plan.md §5.4) and follow the established functional-result pattern.")]
public readonly record struct Result<T>
{
    public bool IsSuccess { get; }
    public T? Value { get; }
    public IReadOnlyList<AppError> Errors { get; }
    public Guid CorrelationId { get; }

    private Result(bool isSuccess, T? value, IReadOnlyList<AppError> errors, Guid correlationId)
    {
        IsSuccess = isSuccess;
        Value = value;
        Errors = errors;
        CorrelationId = correlationId;
    }

    public static Result<T> Ok(T value) => new(true, value, [], Guid.NewGuid());

    public static Result<T> Fail(params AppError[] errors)
    {
        if (errors is null || errors.Length == 0)
        {
            throw new ArgumentException("A failed result requires at least one AppError.", nameof(errors));
        }

        return new Result<T>(false, default, errors, Guid.NewGuid());
    }

    /// <summary>Maps the success value while propagating errors unchanged.</summary>
    public Result<TResult> Map<TResult>(Func<T, TResult> mapper)
    {
        ArgumentNullException.ThrowIfNull(mapper);

        return IsSuccess
            ? Result<TResult>.Ok(mapper(Value!))
            : Result<TResult>.Fail([.. Errors]);
    }

    /// <summary>Chains a follow-up operation whose errors append to these.</summary>
    public Result<TResult> Bind<TResult>(Func<T, Result<TResult>> binder)
    {
        ArgumentNullException.ThrowIfNull(binder);

        if (!IsSuccess)
        {
            return Result<TResult>.Fail([.. Errors]);
        }

        Result<TResult> inner = binder(Value!);
        return inner.IsSuccess
            ? inner
            : Result<TResult>.Fail([.. Errors, .. inner.Errors]);
    }
}
