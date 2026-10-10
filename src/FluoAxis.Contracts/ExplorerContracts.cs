namespace FluoAxis.Contracts;

using System.Text.Json.Serialization;

public sealed record ExplorerPairedPointDto(int PointIndex, long ElapsedTicks, double Fam, double Rox);

public sealed record ExplorerPairPageDto
{
    public Guid RunId { get; }
    public int Revision { get; }
    public string Well { get; }
    public string SourceSha256 { get; }
    public int Offset { get; }
    public int TotalPoints { get; }
    public IReadOnlyList<ExplorerPairedPointDto> Points { get; }
    public ExplorerPairPageDto(Guid runId, int revision, string well, string sourceSha256, int offset, int totalPoints, IReadOnlyList<ExplorerPairedPointDto> points)
    {
        ContractGuard.RequireNonEmptyGuid(runId, nameof(runId));
        ContractGuard.RequireNonNegative(revision, nameof(revision));
        ContractGuard.RequireWellId(well);
        ContractGuard.RequireSha256(sourceSha256, nameof(sourceSha256));
        ArgumentNullException.ThrowIfNull(points);
        if (offset < 0 || totalPoints < 0 || points.Count > 25000 || points.Count > totalPoints) throw new ArgumentException("Invalid Explorer page bounds.");
        if (points.Any(point => point is null || point.PointIndex < 0 || !double.IsFinite(point.Fam) || !double.IsFinite(point.Rox))) throw new ArgumentException("Invalid paired points.");
        for (int i = 1; i < points.Count; i++)
            if (points[i].PointIndex <= points[i - 1].PointIndex || points[i].ElapsedTicks <= points[i - 1].ElapsedTicks) throw new ArgumentException("Paired points must preserve time order.");
        RunId = runId; Revision = revision; Well = well; SourceSha256 = sourceSha256.ToLowerInvariant(); Offset = offset; TotalPoints = totalPoints; Points = Array.AsReadOnly(points.ToArray());
    }
}

/// <summary>Research-only derived column; never consumed by the formal analysis pipeline.</summary>
public sealed record ExplorerDerivedColumnDto
{
    public string Id { get; }
    public string Label { get; }
    public string Unit { get; }
    public string Expression { get; }
    public string Smoothing { get; }
    public int Window { get; }
    public int PolynomialOrder { get; }
    public ExplorerDerivedColumnDto(string id, string label, string unit, string expression, string smoothing = "none", int window = 5, int polynomialOrder = 2)
    {
        ExplorerGuard.Identifier(id);
        ExplorerGuard.Text(label, 80); ExplorerGuard.Text(unit, 24, allowEmpty: true); ExplorerGuard.Text(expression, 1024);
        if (smoothing is not ("none" or "movingAverage" or "savitzkyGolay")) throw new ArgumentException("Unsupported smoothing.");
        if (window is < 3 or > 51 || window % 2 == 0 || polynomialOrder is not (2 or 3) || window <= polynomialOrder) throw new ArgumentException("Invalid visual smoothing window/order.");
        Id = id; Label = label; Unit = unit; Expression = expression; Smoothing = smoothing; Window = window; PolynomialOrder = polynomialOrder;
    }
}

public sealed record ExplorerColumnLabelDto(string Id, string Label, string Unit);
public sealed record ExplorerCellOverrideDto(string PointId, string ColumnId, double Value);
public sealed record ExplorerManualPointDto(int PointIndex, double X, double Y, double Z);

/// <summary>Persisted display/research definition. No raw values are overwritten, and no clinical or formal analysis fields exist.</summary>
public sealed record ExplorerViewDefinitionDto
{
    public const int CurrentSchemaVersion = 1;
    public int SchemaVersion { get; }
    public Guid RunId { get; }
    public string? SourceSha256 { get; }
    public string Well { get; }
    public string Mode { get; }
    public string PlotKind { get; }
    public string XColumn { get; }
    public string YColumn { get; }
    public string ZColumn { get; }
    public string XScale { get; }
    public string YScale { get; }
    public string ZScale { get; }
    public string? SecondaryYColumn { get; }
    public string? ErrorYColumn { get; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? FilterColumn { get; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public double? FilterMinimum { get; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public double? FilterMaximum { get; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? SortColumn { get; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public bool SortDescending { get; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public int PageOffset { get; }
    public IReadOnlyList<ExplorerDerivedColumnDto> DerivedColumns { get; }
    public IReadOnlyList<ExplorerColumnLabelDto> Labels { get; }
    public IReadOnlyList<ExplorerCellOverrideDto> Overrides { get; }
    public IReadOnlyList<ExplorerManualPointDto> ManualPoints { get; }
    public ExplorerViewDefinitionDto(Guid runId, string? sourceSha256, string well, string mode, string plotKind,
        string xColumn, string yColumn, string zColumn, string xScale, string yScale, string zScale,
        IReadOnlyList<ExplorerDerivedColumnDto> derivedColumns, IReadOnlyList<ExplorerColumnLabelDto> labels,
        IReadOnlyList<ExplorerCellOverrideDto> overrides, IReadOnlyList<ExplorerManualPointDto> manualPoints, int schemaVersion = CurrentSchemaVersion,
        string? secondaryYColumn = null, string? errorYColumn = null, string? filterColumn = null,
        double? filterMinimum = null, double? filterMaximum = null, string? sortColumn = null, bool sortDescending = false, int pageOffset = 0)
    {
        ContractGuard.RequireNonEmptyGuid(runId, nameof(runId)); ContractGuard.RequireWellId(well);
        if (sourceSha256 is not null) ContractGuard.RequireSha256(sourceSha256, nameof(sourceSha256));
        if (schemaVersion != CurrentSchemaVersion || mode is not ("raw" or "manual") || plotKind is not ("Scatter2D" or "Line2D" or "ScatterLine2D" or "Scatter3D" or "Line3D")) throw new ArgumentException("Unsupported Explorer view version or mode.");
        if (mode == "raw" && sourceSha256 is null) throw new ArgumentException("Raw view requires source identity.");
        foreach (string id in new[] { xColumn, yColumn, zColumn }) ExplorerGuard.Identifier(id);
        foreach (string scale in new[] { xScale, yScale, zScale }) if (scale is not ("Linear" or "Log")) throw new ArgumentException("Unsupported axis scale.");
        ArgumentNullException.ThrowIfNull(derivedColumns); ArgumentNullException.ThrowIfNull(labels); ArgumentNullException.ThrowIfNull(overrides); ArgumentNullException.ThrowIfNull(manualPoints);
        if (labels.Any(label => label is null) || overrides.Any(item => item is null)) throw new ArgumentException("Explorer fields must not be null.");
        if (derivedColumns.Count > 8 || labels.Count > 12 || overrides.Count > 5000 || manualPoints.Count > 10000) throw new ArgumentException("Explorer view exceeds its resource budget.");
        if (derivedColumns.Any(column => column is null) || derivedColumns.Select(column => column.Id).Distinct(StringComparer.Ordinal).Count() != derivedColumns.Count) throw new ArgumentException("Duplicate derived column.");
        string[] rawIds = mode == "raw" ? ["index", "time", "fam", "rox"] : ["index", "x", "y", "z"];
        if (derivedColumns.Any(column => rawIds.Contains(column.Id, StringComparer.Ordinal))) throw new ArgumentException("A derived column cannot replace a raw column.");
        var available = rawIds.Concat(derivedColumns.Select(column => column.Id)).ToHashSet(StringComparer.Ordinal);
        if (new[] { xColumn, yColumn, zColumn }.Any(id => !available.Contains(id))) throw new ArgumentException("An axis references an unknown column.");
        if (secondaryYColumn is not null && !available.Contains(secondaryYColumn) || errorYColumn is not null && !available.Contains(errorYColumn)) throw new ArgumentException("Unknown secondary or uncertainty column.");
        if (filterColumn is not null && !available.Contains(filterColumn) || sortColumn is not null && !available.Contains(sortColumn)
            || filterMinimum is { } low && !double.IsFinite(low) || filterMaximum is { } high && !double.IsFinite(high)
            || filterMinimum > filterMaximum || pageOffset < 0 || mode == "manual" && pageOffset != 0) throw new ArgumentException("Invalid Explorer filter, order or page.");
        if (labels.Select(label => label.Id).Distinct(StringComparer.Ordinal).Count() != labels.Count
            || overrides.Select(item => (item.PointId, item.ColumnId)).Distinct().Count() != overrides.Count) throw new ArgumentException("Duplicate Explorer label or edit.");
        foreach (ExplorerColumnLabelDto label in labels) { ArgumentNullException.ThrowIfNull(label); ExplorerGuard.Identifier(label.Id); ExplorerGuard.Text(label.Label, 80); ExplorerGuard.Text(label.Unit, 24, true); if (!available.Contains(label.Id)) throw new ArgumentException("Unknown labeled column."); }
        foreach (ExplorerCellOverrideDto item in overrides)
        {
            ArgumentNullException.ThrowIfNull(item); ExplorerGuard.Text(item.PointId, 128); ExplorerGuard.Identifier(item.ColumnId);
            if (!double.IsFinite(item.Value) || !available.Contains(item.ColumnId) || (mode == "raw" && rawIds.Contains(item.ColumnId, StringComparer.Ordinal)) || item.ColumnId == "index") throw new ArgumentException("Raw or unknown values cannot be edited.");
        }
        if (manualPoints.Any(point => point is null || point.PointIndex < 0 || !double.IsFinite(point.X) || !double.IsFinite(point.Y) || !double.IsFinite(point.Z))
            || manualPoints.Select(point => point.PointIndex).Distinct().Count() != manualPoints.Count) throw new ArgumentException("Invalid manual points.");
        if (mode == "raw" && manualPoints.Count != 0 || mode == "manual" && sourceSha256 is not null) throw new ArgumentException("Raw and manual identities must remain separate.");
        SchemaVersion = schemaVersion; RunId = runId; SourceSha256 = sourceSha256?.ToLowerInvariant(); Well = well; Mode = mode; PlotKind = plotKind;
        XColumn = xColumn; YColumn = yColumn; ZColumn = zColumn; XScale = xScale; YScale = yScale; ZScale = zScale;
        SecondaryYColumn = secondaryYColumn; ErrorYColumn = errorYColumn;
        FilterColumn = filterColumn; FilterMinimum = filterMinimum; FilterMaximum = filterMaximum; SortColumn = sortColumn; SortDescending = sortDescending; PageOffset = pageOffset;
        DerivedColumns = Array.AsReadOnly(derivedColumns.ToArray()); Labels = Array.AsReadOnly(labels.ToArray()); Overrides = Array.AsReadOnly(overrides.ToArray()); ManualPoints = Array.AsReadOnly(manualPoints.ToArray());
    }
}

public sealed record SavedExplorerViewDto
{
    public ExplorerViewDefinitionDto Definition { get; }
    public string Sha256 { get; }
    public SavedExplorerViewDto(ExplorerViewDefinitionDto definition, string sha256)
    {
        ArgumentNullException.ThrowIfNull(definition); ContractGuard.RequireSha256(sha256, nameof(sha256)); Definition = definition; Sha256 = sha256.ToLowerInvariant();
    }
}

internal static class ExplorerGuard
{
    internal static void Identifier(string value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > 32 || !char.IsAsciiLetter(value[0]) || value.Any(ch => !char.IsAsciiLetterOrDigit(ch) && ch != '_')) throw new ArgumentException("Invalid Explorer column identifier.");
    }
    internal static void Text(string value, int maximum, bool allowEmpty = false)
    {
        ArgumentNullException.ThrowIfNull(value);
        if ((!allowEmpty && string.IsNullOrWhiteSpace(value)) || value.Length > maximum || value.Any(char.IsControl)
            || value.Contains('\\') || value.StartsWith('/') || value.Contains(":/", StringComparison.Ordinal) || value.Contains("file:", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Invalid Explorer text.");
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(ExplorerViewDefinitionDto))]
[JsonSerializable(typeof(SavedExplorerViewDto))]
public sealed partial class ExplorerJsonContext : JsonSerializerContext;
