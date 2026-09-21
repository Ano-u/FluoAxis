namespace FluoAxis.Contracts;

using System.Collections.Frozen;

/// <summary>
/// Closed camelCase string vocabularies of the wire contract (plan.md §5).
/// Enum-like values cross the boundary as plain strings so the JSON
/// contract never depends on C# enum member spelling. Error codes are the
/// exception: they remain SCREAMING_SNAKE_CASE per plan.md §5.4 and are
/// validated separately. Every vocabulary member validated here is also
/// the exact JSON token.
/// </summary>
public static class SeverityNames
{
    public const string Info = "info";
    public const string Warning = "warning";
    public const string Blocking = "blocking";
    public const string Fatal = "fatal";

    public static FrozenSet<string> All { get; } = new[] { Info, Warning, Blocking, Fatal }.ToFrozenSet();
}

/// <summary>
/// Actions the frontend may offer for a run, derived from the backend's
/// <c>allowedActions</c> list (plan.md §5.3). The UI never recomputes these
/// from the domain state itself.
/// </summary>
public static class RunAction
{
    public const string Import = "import";
    public const string ConfirmMapping = "confirmMapping";
    public const string AssignWells = "assignWells";
    public const string RunCalibration = "runCalibration";
    public const string RunControlQc = "runControlQc";
    public const string AnalyzeSamples = "analyzeSamples";
    public const string Export = "export";
    public const string ViewExplorer = "viewExplorer";

    public static FrozenSet<string> All { get; } = new[]
    {
        Import, ConfirmMapping, AssignWells, RunCalibration, RunControlQc, AnalyzeSamples, Export, ViewExplorer,
    }.ToFrozenSet();
}

/// <summary>Stages reported by the read-only import inspection (plan.md §7.1).</summary>
public static class ImportStageNames
{
    public const string DetectingFormat = "detectingFormat";
    public const string ReadingSheet = "readingSheet";
    public const string DiscoveringRegion = "discoveringRegion";
    public const string Validating = "validating";
    public const string Hashing = "hashing";

    public static FrozenSet<string> All { get; } = new[]
    {
        DetectingFormat, ReadingSheet, DiscoveringRegion, Validating, Hashing,
    }.ToFrozenSet();
}

/// <summary>
/// Call of a well, group, or sample relative to the positive/negative
/// decision vocabulary (CLAUDE.md §3: positive/negative research labels
/// only, never clinical wording).
/// </summary>
public static class CallNames
{
    public const string Positive = "positive";
    public const string Negative = "negative";
    public const string NotDetermined = "notDetermined";

    public static FrozenSet<string> All { get; } = new[] { Positive, Negative, NotDetermined }.ToFrozenSet();
}

/// <summary>Run-level and stage-level validity vocabulary (plan.md §5.5).</summary>
public static class StatusNames
{
    public const string Pass = "pass";
    public const string Fail = "fail";
    public const string Invalid = "invalid";
    public const string Valid = "valid";

    public static FrozenSet<string> All { get; } = new[] { Pass, Fail, Invalid, Valid }.ToFrozenSet();
}

/// <summary>Dual acquisition channels of the BRAF V600E research workflow.</summary>
public static class ChannelNames
{
    public const string Fam = "fam";
    public const string Rox = "rox";

    public static FrozenSet<string> All { get; } = new[] { Fam, Rox }.ToFrozenSet();
}
