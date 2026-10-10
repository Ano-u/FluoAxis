namespace FluoAxis.Presentation;

using System.Globalization;
using FluoAxis.Contracts;

/// <summary>Editable presentation data; blank/invalid fields remain drafts until the user submits.</summary>
public sealed record WellDraft(string Well, string? Role = null, string SampleId = "", string Group = "",
    double ReplicateIndex = 1, string Target = "", string Notes = "");

/// <summary>Only checked fields are applied. A blank checked value intentionally clears that field.</summary>
public sealed record PlateBatchEdit(string? Role = null, bool ApplySampleId = false, string SampleId = "",
    bool ApplyGroup = false, string Group = "", bool ApplyTarget = false, string Target = "",
    bool ApplyNotes = false, string Notes = "", bool AssignReplicateSequence = false, int FirstReplicate = 1);

/// <summary>Bounded plate editing and selection with no scientific or filesystem behavior.</summary>
public sealed class PlateDraftEditor
{
    private const int HistoryLimit = 20;
    private readonly Dictionary<string, WellDraft> rows = new(StringComparer.Ordinal);
    private readonly HashSet<string> selection = new(StringComparer.Ordinal);
    private readonly List<WellDraft[]> undo = [];
    private readonly List<WellDraft[]> redo = [];
    private WellDraft[] baseline = [];
    private string? anchor;

    public IReadOnlyList<WellDraft> Rows => Array.AsReadOnly(rows.Values.OrderBy(row => PlateIndex(row.Well)).ToArray());
    public IReadOnlyList<string> SelectedWells => Array.AsReadOnly(selection.OrderBy(PlateIndex).ToArray());
    public bool IsDirty => !baseline.SequenceEqual(rows.Values.OrderBy(row => PlateIndex(row.Well)));
    public bool CanUndo => undo.Count > 0;
    public bool CanRedo => redo.Count > 0;

    public void Load(IEnumerable<string> wells, IReadOnlyList<WellAssignmentDto>? assignments = null)
    {
        ArgumentNullException.ThrowIfNull(wells);
        rows.Clear();
        foreach (string well in wells)
        {
            _ = PlateIndex(well);
            rows.Add(well, new WellDraft(well));
        }
        foreach (WellAssignmentDto saved in assignments ?? [])
            if (rows.ContainsKey(saved.Well)) rows[saved.Well] = new WellDraft(saved.Well, saved.Role, saved.SampleId ?? "",
                saved.Group ?? "", saved.ReplicateIndex ?? 1, saved.Target ?? "", saved.Notes ?? "");
        selection.Clear();
        anchor = null;
        AcceptChanges();
    }

    public void Update(WellDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        if (!rows.TryGetValue(draft.Well, out WellDraft? previous)) throw new ArgumentException("The well is not in the mapped plate.", nameof(draft));
        if (previous == draft) return;
        rows[draft.Well] = draft;
        undo.Clear();
        redo.Clear();
    }

    /// <summary>Applies an explicitly selected profile as one reversible edit. Rejects invalid batches atomically.</summary>
    public void ApplyAssignments(IReadOnlyList<WellAssignmentDto> assignments)
    {
        ArgumentNullException.ThrowIfNull(assignments);
        if (assignments.Count > 96 || assignments.Any(item => item is null || !rows.ContainsKey(item.Well))
            || assignments.Select(item => item.Well).Distinct(StringComparer.Ordinal).Count() != assignments.Count)
            throw new ArgumentException("Assignments must use unique wells from the current mapped plate.", nameof(assignments));
        WellDraft[] before = Snapshot();
        foreach (WellAssignmentDto item in assignments)
            rows[item.Well] = new WellDraft(item.Well, item.Role, item.SampleId ?? "", item.Group ?? "",
                item.ReplicateIndex ?? 1, item.Target ?? "", item.Notes ?? "");
        if (!before.SequenceEqual(Snapshot())) { Push(undo, before); redo.Clear(); }
    }

    public void Select(string well, bool toggle = false, bool extend = false)
    {
        if (!rows.ContainsKey(well)) return;
        if (extend && anchor is not null)
        {
            int first = PlateIndex(anchor);
            int last = PlateIndex(well);
            if (!toggle) selection.Clear();
            foreach (string candidate in rows.Keys)
            {
                int index = PlateIndex(candidate);
                if (index / 12 >= Math.Min(first / 12, last / 12) && index / 12 <= Math.Max(first / 12, last / 12)
                    && index % 12 >= Math.Min(first % 12, last % 12) && index % 12 <= Math.Max(first % 12, last % 12))
                    selection.Add(candidate);
            }
            return;
        }
        if (!toggle) selection.Clear();
        if (toggle && !selection.Add(well)) selection.Remove(well);
        else selection.Add(well);
        anchor = well;
    }

    public void SelectAll() { selection.UnionWith(rows.Keys); }
    public void ClearSelection() { selection.Clear(); anchor = null; }

    public string? Adjacent(string well, int rowDelta, int columnDelta)
    {
        int current = PlateIndex(well);
        int row = current / 12 + rowDelta;
        int column = current % 12 + columnDelta;
        if (row is < 0 or > 7 || column is < 0 or > 11) return null;
        string candidate = $"{(char)('A' + row)}{column + 1}";
        return rows.ContainsKey(candidate) ? candidate : null;
    }

    public void ApplyBatch(PlateBatchEdit edit)
    {
        ArgumentNullException.ThrowIfNull(edit);
        if (selection.Count == 0) return;
        if (edit.AssignReplicateSequence && (edit.FirstReplicate < 1 || (long)edit.FirstReplicate + selection.Count - 1 > int.MaxValue))
            throw new ArgumentOutOfRangeException(nameof(edit), "Replicate indices must remain positive integers.");
        if (edit.Role is not null && edit.Role is not (WellRoleNames.Ntc or WellRoleNames.Positive100 or WellRoleNames.WildType
            or WellRoleNames.Lqc01Percent or WellRoleNames.Sample or WellRoleNames.Ignore))
            throw new ArgumentException("Select a supported role.", nameof(edit));
        WellDraft[] before = Snapshot();
        int replicate = edit.FirstReplicate;
        foreach (string well in SelectedWells)
        {
            WellDraft current = rows[well];
            rows[well] = current with
            {
                Role = edit.Role ?? current.Role,
                SampleId = edit.ApplySampleId ? edit.SampleId : current.SampleId,
                Group = edit.ApplyGroup ? edit.Group : current.Group,
                Target = edit.ApplyTarget ? edit.Target : current.Target,
                Notes = edit.ApplyNotes ? edit.Notes : current.Notes,
                ReplicateIndex = edit.AssignReplicateSequence ? replicate++ : current.ReplicateIndex,
            };
        }
        if (!before.SequenceEqual(Snapshot())) { Push(undo, before); redo.Clear(); }
    }

    public void Undo() => RestoreHistory(undo, redo);
    public void Redo() => RestoreHistory(redo, undo);

    public void AcceptChanges()
    {
        baseline = Snapshot();
        undo.Clear();
        redo.Clear();
    }

    public IReadOnlyList<WellAssignmentDto> ToAssignments() => Rows.Select(row =>
    {
        if (row.Role is null) throw new ArgumentException($"孔 {row.Well} 尚未选择角色。");
        bool isSample = row.Role == WellRoleNames.Sample;
        if (isSample && (!double.IsFinite(row.ReplicateIndex) || row.ReplicateIndex < 1
            || row.ReplicateIndex > int.MaxValue || row.ReplicateIndex != Math.Truncate(row.ReplicateIndex)))
            throw new ArgumentException($"孔 {row.Well} 的重复孔序号必须为正整数。");
        return new WellAssignmentDto(row.Well, row.Role, isSample ? row.SampleId.Trim() : null,
            isSample ? Optional(row.Group) : null, isSample ? (int)row.ReplicateIndex : null,
            isSample ? Optional(row.Target) : null, isSample ? Optional(row.Notes) : null);
    }).ToArray();

    private void RestoreHistory(List<WellDraft[]> source, List<WellDraft[]> destination)
    {
        if (source.Count == 0) return;
        Push(destination, Snapshot());
        WellDraft[] previous = source[^1];
        source.RemoveAt(source.Count - 1);
        foreach (WellDraft draft in previous) rows[draft.Well] = draft;
    }

    private WellDraft[] Snapshot() => rows.Values.OrderBy(row => PlateIndex(row.Well)).ToArray();
    private static string? Optional(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static void Push(List<WellDraft[]> history, WellDraft[] value)
    {
        if (history.Count == HistoryLimit) history.RemoveAt(0);
        history.Add(value);
    }

    private static int PlateIndex(string well)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(well);
        if (well.Length is < 2 or > 3 || well[0] is < 'A' or > 'H'
            || !int.TryParse(well.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out int column) || column is < 1 or > 12
            || well != $"{well[0]}{column}") throw new ArgumentException("A mapped well must be A1 through H12.", nameof(well));
        return (well[0] - 'A') * 12 + column - 1;
    }
}
