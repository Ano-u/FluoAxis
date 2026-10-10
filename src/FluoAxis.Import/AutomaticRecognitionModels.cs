namespace FluoAxis.Import;

using FluoAxis.Domain;

/// <summary>Explicit or recognized parsing choices; records text names without a UI dependency.</summary>
public sealed record RecognitionParsingOptions(string EncodingName, string Delimiter, string DecimalCulture, string TimeUnit);

public sealed record RecognitionIssue(string Code, string Message);

/// <summary>Explicit file role metadata. Missing metadata is never filled from RFU values.</summary>
public sealed record RecognizedWellRole(WellId Well, WellRole Role, string? SampleId = null, int? ReplicateIndex = null);

public sealed record AutomaticRecognitionResult(
    ImportFileFormat Format,
    RecognitionParsingOptions? Options,
    ChannelRegionMapping? Fam,
    ChannelRegionMapping? Rox,
    IReadOnlyList<RecognizedWellRole> Assignments,
    IReadOnlyList<RecognitionIssue> Issues,
    IReadOnlyList<string> Reasons,
    bool MappingReady,
    bool RequiresReview);
