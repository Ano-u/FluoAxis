namespace FluoAxis.Contracts;

using FluoAxis.Domain;

/// <summary>
/// Wire error of a gateway operation (plan.md §5.4). Codes are the
/// compatibility contract and follow the namespace pattern validated at
/// construction. Severity crosses the boundary as a lowercase string
/// ("info" | "warning" | "blocking" | "fatal") so the JSON contract does
/// not depend on C# enum spelling.
/// </summary>
public sealed record AppErrorDto
{
    public string Code { get; }
    public string Severity { get; }
    public string UserMessage { get; }
    public string? RecommendedAction { get; }
    public string? FieldPath { get; }
    public string? WellId { get; }
    public string? Channel { get; }
    public string? TechnicalReference { get; }

    public AppErrorDto(
        string code,
        string severity,
        string userMessage,
        string? recommendedAction = null,
        string? fieldPath = null,
        string? wellId = null,
        string? channel = null,
        string? technicalReference = null)
    {
        ContractGuard.RequireErrorCode(code);
        ContractGuard.RequireInVocabulary(severity, SeverityNames.All, nameof(severity), "Severity");
        ContractGuard.RequireNonBlank(userMessage, nameof(userMessage));

        if (wellId is not null)
        {
            Domain.WellId.Parse(wellId); // throws FormatException outside A1..H12
        }

        if (channel is not null && channel != ChannelNames.Fam && channel != ChannelNames.Rox)
        {
            throw new ArgumentException(
                $"'{channel}' is not a valid channel; expected 'fam' or 'rox'.",
                nameof(channel));
        }

        Code = code;
        Severity = severity;
        UserMessage = userMessage;
        RecommendedAction = recommendedAction;
        FieldPath = fieldPath;
        WellId = wellId;
        Channel = channel;
        TechnicalReference = technicalReference;
    }
}

/// <summary>
/// Non-generic factory surface for <see cref="GatewayResult{T}"/> so static
/// members stay off the generic type (CA1000).
/// </summary>
public static class GatewayResult
{
    public static GatewayResult<T> Ok<T>(T value) => new(true, value, Array.Empty<AppErrorDto>(), Guid.NewGuid());

    public static GatewayResult<T> Fail<T>(params AppErrorDto[] errors)
    {
        if (errors is null || errors.Length == 0)
        {
            throw new ArgumentException("A failed result requires at least one AppErrorDto.", nameof(errors));
        }

        return new GatewayResult<T>(false, default, errors, Guid.NewGuid());
    }
}

/// <summary>
/// Uniform gateway response envelope (plan.md §5.4): success with a value,
/// or failure with a non-empty error list. A failed result never carries a
/// value, mirroring Domain's <see cref="Domain.Result{T}"/> semantics.
/// Create instances via the non-generic <see cref="GatewayResult"/> factory.
/// </summary>
public sealed record GatewayResult<T>
{
    public bool IsSuccess { get; }
    public T? Value { get; }
    public IReadOnlyList<AppErrorDto> Errors { get; }
    public Guid CorrelationId { get; }

    internal GatewayResult(bool isSuccess, T? value, IReadOnlyList<AppErrorDto> errors, Guid correlationId)
    {
        IsSuccess = isSuccess;
        Value = value;
        Errors = errors;
        CorrelationId = correlationId;
    }
}

/// <summary>
/// Bidirectional mapping between the wire <see cref="AppErrorDto"/> and the
/// domain <see cref="Domain.AppError"/>. The Application layer uses these
/// to assemble gateway responses from domain results without the
/// Presentation layer ever seeing domain types.
/// </summary>
public static class AppErrorMappingExtensions
{
    /// <summary>Domain error to wire DTO.</summary>
    public static AppErrorDto ToDto(this Domain.AppError error, string? recommendedAction = null) => new(
        error.Code,
        SeverityFromDomain(error.Severity),
        error.UserMessage,
        recommendedAction,
        fieldPath: null,
        wellId: null,
        channel: null,
        technicalReference: error.SafeDetailCode);

    /// <summary>Wire DTO back to the domain error (severity round-trips exactly).</summary>
    public static Domain.AppError ToDomain(this AppErrorDto error) => new(
        error.Code,
        SeverityToDomain(error.Severity),
        error.UserMessage,
        error.TechnicalReference);

    private static string SeverityFromDomain(Domain.ErrorSeverity severity) => severity switch
    {
        Domain.ErrorSeverity.Info => SeverityNames.Info,
        Domain.ErrorSeverity.Warning => SeverityNames.Warning,
        Domain.ErrorSeverity.Blocking => SeverityNames.Blocking,
        Domain.ErrorSeverity.Fatal => SeverityNames.Fatal,
        _ => throw new ArgumentOutOfRangeException(nameof(severity), severity, "Unknown ErrorSeverity member."),
    };

    private static Domain.ErrorSeverity SeverityToDomain(string severity) => severity switch
    {
        SeverityNames.Info => Domain.ErrorSeverity.Info,
        SeverityNames.Warning => Domain.ErrorSeverity.Warning,
        SeverityNames.Blocking => Domain.ErrorSeverity.Blocking,
        SeverityNames.Fatal => Domain.ErrorSeverity.Fatal,
        _ => throw new ArgumentException(
            $"'{severity}' is not a member of the Severity contract vocabulary.",
            nameof(severity)),
    };
}

/// <summary>
/// Convenience mapping from a domain <see cref="Domain.Result{T}"/> to the
/// gateway envelope, preserving the domain correlation id.
/// </summary>
public static class GatewayResultMappingExtensions
{
    public static GatewayResult<TTarget> ToGatewayResult<T, TTarget>(
        this Domain.Result<T> result,
        Func<T, TTarget> mapper)
        => result.ToGatewayResult(mapper, error => error.ToDto());

    public static GatewayResult<TTarget> ToGatewayResult<T, TTarget>(
        this Domain.Result<T> result,
        Func<T, TTarget> mapper,
        Func<Domain.AppError, AppErrorDto> errorMapper)
    {
        ArgumentNullException.ThrowIfNull(mapper);
        ArgumentNullException.ThrowIfNull(errorMapper);

        if (result.IsSuccess)
        {
            return GatewayResult.Ok(mapper(result.Value!));
        }

        AppErrorDto[] errors = result.Errors.Select(errorMapper).ToArray();
        return new GatewayResult<TTarget>(false, default, errors, result.CorrelationId);
    }
}
