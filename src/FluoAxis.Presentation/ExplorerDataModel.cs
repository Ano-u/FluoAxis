namespace FluoAxis.Presentation;

using FluoAxis.Contracts;

public sealed record ExplorerColumn(string Id, string Label, string Unit, bool IsRaw);
public sealed record ExplorerColumnSummary(int Count, int Missing, double? Minimum, double? Maximum, double? Mean, double? Median, double? SampleStandardDeviation);

/// <summary>Bounded research-only page, with separate raw arrays and derived values. Never enters Guided commands.</summary>
public sealed class ExplorerDataModel
{
    public const int MaximumOperations = 2_000_000;
    private readonly Dictionary<string, double?[]> baseValues;
    private Dictionary<string, double?[]> values;
    private readonly string[] pointIds;
    private readonly ExplorerColumn[] baseColumns;
    private ExplorerColumn[] columns;
    public Guid RunId { get; }
    public string Well { get; }
    public string? SourceSha256 { get; }
    public bool IsManual { get; }
    public int Count => pointIds.Length;
    public IReadOnlyList<ExplorerColumn> Columns => Array.AsReadOnly(columns);
    public IReadOnlyList<string> PointIds => Array.AsReadOnly(pointIds);
    public int InvalidDerivedValues { get; private set; }

    private ExplorerDataModel(Guid runId, string well, string? sourceSha256, bool manual, string[] ids, Dictionary<string, double?[]> data, ExplorerColumn[] columns)
    {
        RunId = runId; Well = well; SourceSha256 = sourceSha256; IsManual = manual; pointIds = ids; baseValues = data;
        values = new Dictionary<string, double?[]>(data, StringComparer.Ordinal); this.columns = columns; baseColumns = columns;
    }
    public static ExplorerDataModel FromPaired(ExplorerPairPageDto page)
    {
        ArgumentNullException.ThrowIfNull(page);
        return new(page.RunId, page.Well, page.SourceSha256, false,
            page.Points.Select(point => $"paired-{page.RunId:N}-{page.Well}-{point.PointIndex}").ToArray(),
            new(StringComparer.Ordinal)
            {
                ["index"] = page.Points.Select(point => (double?)point.PointIndex).ToArray(),
                ["time"] = page.Points.Select(point => (double?)point.ElapsedTicks / TimeSpan.TicksPerMinute).ToArray(),
                ["fam"] = page.Points.Select(point => (double?)point.Fam).ToArray(), ["rox"] = page.Points.Select(point => (double?)point.Rox).ToArray(),
            }, [new("index", "PointIndex", "", true), new("time", "经过时间", "min", true), new("fam", "FAM", "RFU", true), new("rox", "ROX", "RFU", true)]);
    }
    public static ExplorerDataModel FromManual(Guid runId, IReadOnlyList<ExplorerManualPointDto> points)
    {
        ArgumentNullException.ThrowIfNull(points);
        if (points.Count > 10000 || points.Any(point => point is null || point.PointIndex < 0 || !double.IsFinite(point.X) || !double.IsFinite(point.Y) || !double.IsFinite(point.Z))
            || points.Select(point => point.PointIndex).Distinct().Count() != points.Count) throw new ArgumentException("手工表最多 10,000 行有限数值，行身份必须唯一。");
        return new(runId, "A1", null, true, points.Select(point => $"manual-{runId:N}-{point.PointIndex}").ToArray(),
            new(StringComparer.Ordinal)
            {
                ["index"] = points.Select(point => (double?)point.PointIndex).ToArray(), ["x"] = points.Select(point => (double?)point.X).ToArray(),
                ["y"] = points.Select(point => (double?)point.Y).ToArray(), ["z"] = points.Select(point => (double?)point.Z).ToArray(),
            }, [new("index", "Row", "", true), new("x", "X", "", false), new("y", "Y", "", false), new("z", "Z", "", false)]);
    }

    public void Apply(ExplorerViewDefinitionDto definition, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (definition.RunId != RunId || definition.Mode != (IsManual ? "manual" : "raw") || definition.SourceSha256 != SourceSha256) throw new ArgumentException("视图不属于当前数据来源。");
        var next = new Dictionary<string, double?[]>(baseValues, StringComparer.Ordinal);
        var nextColumns = baseColumns.ToList();
        int budget = MaximumOperations;
        var overrides = definition.Overrides.GroupBy(item => item.ColumnId).ToDictionary(group => group.Key, group => group.ToDictionary(item => item.PointId, item => item.Value, StringComparer.Ordinal), StringComparer.Ordinal);
        if (IsManual)
            foreach (string id in new[] { "x", "y", "z" })
            {
                next[id] = (double?[])baseValues[id].Clone();
                ApplyOverrides(id, next[id], overrides);
            }
        foreach (ExplorerDerivedColumnDto column in definition.DerivedColumns)
        {
            token.ThrowIfCancellationRequested();
            ExplorerExpression expression = ExplorerExpression.Parse(column.Expression, next.Keys.ToHashSet(StringComparer.Ordinal));
            var output = new double?[Count];
            for (int row = 0; row < Count; row++)
            {
                if ((row & 127) == 0) token.ThrowIfCancellationRequested();
                output[row] = expression.Evaluate(next, row, ref budget);
            }
            if (column.Smoothing != "none") output = Smooth(output, column.Smoothing, column.Window, column.PolynomialOrder, token);
            ApplyOverrides(column.Id, output, overrides);
            next.Add(column.Id, output);
            nextColumns.Add(new ExplorerColumn(column.Id, column.Label, column.Unit, false));
        }
        foreach (ExplorerColumnLabelDto label in definition.Labels)
        {
            int index = nextColumns.FindIndex(column => column.Id == label.Id);
            if (index >= 0) nextColumns[index] = nextColumns[index] with { Label = label.Label, Unit = label.Unit };
        }
        token.ThrowIfCancellationRequested();
        values = next; columns = nextColumns.ToArray();
        InvalidDerivedValues = columns.Where(column => !column.IsRaw).Sum(column => next[column.Id].Count(value => value is null));
    }

    public double? Value(string column, int row) => values.TryGetValue(column, out double?[]? items) && row >= 0 && row < items.Length ? items[row] : null;
    public string PointId(int row) => pointIds[row];
    public int IndexOfPoint(string id) => Array.IndexOf(pointIds, id);
    public bool CanEdit(string column) => columns.Any(item => item.Id == column && !item.IsRaw);

    public int[] SelectRows(string? filterColumn = null, double? minimum = null, double? maximum = null, string? sortColumn = null, bool descending = false)
    {
        IEnumerable<int> rows = Enumerable.Range(0, Count);
        if (filterColumn is not null)
            rows = rows.Where(row => Value(filterColumn, row) is { } value && (!minimum.HasValue || value >= minimum) && (!maximum.HasValue || value <= maximum));
        if (sortColumn is not null) rows = descending ? rows.OrderByDescending(row => Value(sortColumn, row)) : rows.OrderBy(row => Value(sortColumn, row));
        return rows.ToArray();
    }

    public ExplorerColumnSummary Summarize(string column, IReadOnlyList<int> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        if (rows.Count > 25000 || rows.Any(row => row < 0 || row >= Count)) throw new ArgumentException("统计只接受当前有界页的行。");
        double[] finite = rows.Select(row => Value(column, row)).Where(value => value.HasValue).Select(value => value!.Value).Order().ToArray();
        if (finite.Length == 0) return new(0, rows.Count, null, null, null, null, null);
        double scale = Math.Max(Math.Abs(finite[0]), Math.Abs(finite[^1]));
        double normalizedMean = scale == 0 ? 0 : finite.Sum(value => value / scale / finite.Length);
        double mean = normalizedMean * scale;
        double variance = scale == 0 ? 0 : finite.Sum(value => { double delta = value / scale - normalizedMean; return delta * delta; });
        double sd = finite.Length < 2 ? double.NaN : scale * Math.Sqrt(variance / (finite.Length - 1));
        int mid = finite.Length / 2;
        double median = finite.Length % 2 == 1 ? finite[mid] : finite[mid - 1] / 2 + finite[mid] / 2;
        return new(finite.Length, rows.Count - finite.Length, finite[0], finite[^1], double.IsFinite(mean) ? mean : null, median, double.IsFinite(sd) ? sd : null);
    }

    private void ApplyOverrides(string id, double?[] output, Dictionary<string, Dictionary<string, double>> overrides)
    {
        if (!overrides.TryGetValue(id, out Dictionary<string, double>? edits)) return;
        for (int row = 0; row < output.Length; row++) if (edits.TryGetValue(pointIds[row], out double value)) output[row] = value;
    }

    /// <summary>Centered windows use a shifted full window at page edges. SG fits row positions, not physical time.</summary>
    public static double?[] Smooth(IReadOnlyList<double?> input, string mode, int window, int order, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (window is < 3 or > 51 || window % 2 == 0 || order is not (2 or 3) || window <= order || mode is not ("movingAverage" or "savitzkyGolay")) throw new ArgumentException("视觉平滑参数无效。");
        var result = new double?[input.Count];
        if (input.Count < window) return result;
        var weights = new Dictionary<int, double[]>();
        for (int row = 0; row < input.Count; row++)
        {
            if ((row & 127) == 0) token.ThrowIfCancellationRequested();
            int start = Math.Clamp(row - window / 2, 0, input.Count - window);
            int center = row - start;
            if (!weights.TryGetValue(center, out double[]? rowWeights))
            {
                rowWeights = mode == "movingAverage" ? Enumerable.Repeat(1.0 / window, window).ToArray() : PolynomialWeights(window, order, center);
                weights.Add(center, rowWeights);
            }
            double sum = 0; bool valid = true;
            for (int i = 0; i < window; i++)
            {
                if (input[start + i] is not { } value) { valid = false; break; }
                sum += rowWeights[i] * value;
            }
            if (valid && double.IsFinite(sum)) result[row] = sum;
        }
        return result;
    }

    private static double[] PolynomialWeights(int count, int order, int center)
    {
        int size = order + 1;
        var matrix = new double[size, size + 1];
        for (int row = 0; row < size; row++)
        {
            for (int column = 0; column < size; column++)
                for (int i = 0; i < count; i++) matrix[row, column] += Math.Pow((double)(i - center) / count, row + column);
            matrix[row, size] = row == 0 ? 1 : 0;
        }
        for (int pivot = 0; pivot < size; pivot++)
        {
            int best = pivot;
            for (int row = pivot + 1; row < size; row++) if (Math.Abs(matrix[row, pivot]) > Math.Abs(matrix[best, pivot])) best = row;
            if (Math.Abs(matrix[best, pivot]) < 1e-15) throw new InvalidOperationException("Visual fit is singular.");
            for (int column = 0; column <= size; column++) (matrix[pivot, column], matrix[best, column]) = (matrix[best, column], matrix[pivot, column]);
            double divisor = matrix[pivot, pivot];
            for (int column = pivot; column <= size; column++) matrix[pivot, column] /= divisor;
            for (int row = 0; row < size; row++)
            {
                if (row == pivot) continue;
                double factor = matrix[row, pivot];
                for (int column = pivot; column <= size; column++) matrix[row, column] -= factor * matrix[pivot, column];
            }
        }
        var weights = new double[count];
        for (int i = 0; i < count; i++) for (int term = 0; term < size; term++) weights[i] += matrix[term, size] * Math.Pow((double)(i - center) / count, term);
        return weights;
    }
}
