namespace FluoAxis.Contracts;

/// <summary>
/// One structured blocking or warning issue of a run (plan.md §5.3). Codes
/// follow the plan.md §5.4 namespaces and are validated at construction so
/// the frontend can rely on the error catalog.
/// </summary>
public sealed record IssueDto
{
    public string Code { get; }
    public string UserMessage { get; }
    public string RecommendedAction { get; }
    public string? WellId { get; }
    public string? Channel { get; }

    public IssueDto(
        string code,
        string userMessage,
        string recommendedAction,
        string? wellId = null,
        string? channel = null)
    {
        ContractGuard.RequireErrorCode(code);
        ContractGuard.RequireNonBlank(userMessage, nameof(userMessage));
        ContractGuard.RequireNonBlank(recommendedAction, nameof(recommendedAction));

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
        UserMessage = userMessage;
        RecommendedAction = recommendedAction;
        WellId = wellId;
        Channel = channel;
    }
}

/// <summary>
/// Cross-stage outcome shape shared by calibration, control, and sample
/// analysis summaries. Status strings come from <see cref="StatusNames"/>;
/// a null numeric value means "not computed at this stage".
/// </summary>
public sealed record CalibrationResultDto
{
    public string Status { get; }
    public double? RunFmFam { get; }
    public double? RunFmRox { get; }
    public double? RunFbFam { get; }
    public double? RunFbRox { get; }
    public IReadOnlyList<WellDiagnosticDto> PerWellDiagnostics { get; }

    public CalibrationResultDto(
        string status,
        double? runFmFam = null,
        double? runFmRox = null,
        double? runFbFam = null,
        double? runFbRox = null,
        IReadOnlyList<WellDiagnosticDto>? perWellDiagnostics = null)
    {
        ContractGuard.RequireInVocabulary(status, StatusNames.All, nameof(status), "Status");

        foreach (double? value in new[] { runFmFam, runFmRox, runFbFam, runFbRox })
        {
            if (value is { } v && !double.IsFinite(v))
            {
                throw new ArgumentException("Calibration values must be finite numbers.", nameof(runFmFam));
            }
        }

        Status = status;
        RunFmFam = runFmFam;
        RunFmRox = runFmRox;
        RunFbFam = runFbFam;
        RunFbRox = runFbRox;
        PerWellDiagnostics = perWellDiagnostics ?? [];
    }
}

/// <summary>
/// Per-well calibration diagnostic: plateau relative range, NTC band
/// violation, or missing endpoint, with the offending value when available.
/// </summary>
public sealed record WellDiagnosticDto
{
    public string Well { get; }
    public string Channel { get; }
    public string Code { get; }
    public string Message { get; }
    public double? Value { get; }

    public WellDiagnosticDto(string well, string channel, string code, string message, double? value = null)
    {
        ContractGuard.RequireNonBlank(well, nameof(well));
        ContractGuard.RequireNonBlank(channel, nameof(channel));
        ContractGuard.RequireErrorCode(code);
        ContractGuard.RequireNonBlank(message, nameof(message));

        Domain.WellId.Parse(well);

        if (channel != ChannelNames.Fam && channel != ChannelNames.Rox)
        {
            throw new ArgumentException(
                $"'{channel}' is not a valid channel; expected 'fam' or 'rox'.",
                nameof(channel));
        }

        if (value is { } v && !double.IsFinite(v))
        {
            throw new ArgumentException("Diagnostic values must be finite numbers.", nameof(value));
        }

        Well = well;
        Channel = channel;
        Code = code;
        Message = message;
        Value = value;
    }
}

/// <summary>Outcome of the WT/LQC control QC stage (plan.md §5.1).</summary>
public sealed record ControlQcResultDto
{
    public string Status { get; }
    public double? WtYFamMean { get; }
    public double? WtYRoxMean { get; }
    public string? LqcCall { get; }
    public double? LqcMeanDrPrime { get; }
    public IReadOnlyList<IssueDto> Issues { get; }

    public ControlQcResultDto(
        string status,
        double? wtYFamMean = null,
        double? wtYRoxMean = null,
        string? lqcCall = null,
        double? lqcMeanDrPrime = null,
        IReadOnlyList<IssueDto>? issues = null)
    {
        ContractGuard.RequireInVocabulary(status, StatusNames.All, nameof(status), "Status");

        if (lqcCall is not null && !CallNames.All.Contains(lqcCall))
        {
            throw new ArgumentException(
                $"'{lqcCall}' is not a member of the Call contract vocabulary.",
                nameof(lqcCall));
        }

        foreach (double? value in new[] { wtYFamMean, wtYRoxMean, lqcMeanDrPrime })
        {
            if (value is { } v && !double.IsFinite(v))
            {
                throw new ArgumentException("Control QC values must be finite numbers.", nameof(wtYFamMean));
            }
        }

        Status = status;
        WtYFamMean = wtYFamMean;
        WtYRoxMean = wtYRoxMean;
        LqcCall = lqcCall;
        LqcMeanDrPrime = lqcMeanDrPrime;
        Issues = issues ?? [];
    }
}

/// <summary>
/// Per-sample analysis outcome (plan.md §5.1). A sample whose replicates
/// disagree is <c>invalid</c> with no call; the mean is never allowed to
/// mask a conflict.
/// </summary>
public sealed record SampleAnalysisDto
{
    public string SampleId { get; }
    public string Status { get; }
    public string? Call { get; }
    public double? MeanDrPrime { get; }
    public IReadOnlyList<PerWellResultDto> PerWell { get; }

    public SampleAnalysisDto(
        string sampleId,
        string status,
        string? call = null,
        double? meanDrPrime = null,
        IReadOnlyList<PerWellResultDto>? perWell = null)
    {
        ContractGuard.RequireNonBlank(sampleId, nameof(sampleId));
        ContractGuard.RequireInVocabulary(status, StatusNames.All, nameof(status), "Status");

        if (call is not null && !CallNames.All.Contains(call))
        {
            throw new ArgumentException(
                $"'{call}' is not a member of the Call contract vocabulary.",
                nameof(call));
        }

        if (meanDrPrime is { } v && !double.IsFinite(v))
        {
            throw new ArgumentException("MeanDrPrime must be a finite number.", nameof(meanDrPrime));
        }

        SampleId = sampleId;
        Status = status;
        Call = call;
        MeanDrPrime = meanDrPrime;
        PerWell = perWell ?? [];
    }
}

/// <summary>Per-well classification detail of one sample replicate.</summary>
public sealed record PerWellResultDto
{
    public string Well { get; }
    public double YFam { get; }
    public double YRox { get; }
    public double DrPrime { get; }
    public string Call { get; }
    public string TraceId { get; }

    public PerWellResultDto(string well, double yFam, double yRox, double drPrime, string call, string traceId)
    {
        ContractGuard.RequireNonBlank(well, nameof(well));
        ContractGuard.RequireInVocabulary(call, CallNames.All, nameof(call), "Call");
        ContractGuard.RequireNonBlank(traceId, nameof(traceId));

        Domain.WellId.Parse(well);

        foreach (double value in new[] { yFam, yRox, drPrime })
        {
            if (!double.IsFinite(value))
            {
                throw new ArgumentException("Per-well values must be finite numbers.", nameof(yFam));
            }
        }

        Well = well;
        YFam = yFam;
        YRox = yRox;
        DrPrime = drPrime;
        Call = call;
        TraceId = traceId;
    }
}
