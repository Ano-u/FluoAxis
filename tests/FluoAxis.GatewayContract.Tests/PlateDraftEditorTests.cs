namespace FluoAxis.GatewayContract.Tests;

using FluoAxis.Contracts;
using FluoAxis.Presentation;
using Xunit;

public sealed class PlateDraftEditorTests
{
    [Fact]
    public void MappedWellsStartWithoutInferredRoles()
    {
        var editor = new PlateDraftEditor();
        editor.Load(["A1", "A2"]);
        Assert.All(editor.Rows, well => Assert.Null(well.Role));
        Assert.False(editor.IsDirty);
        Assert.Throws<ArgumentException>(() => editor.ToAssignments());
    }

    [Fact]
    public void ShiftSelectionUsesRectangleAndIgnoresUnmappedWells()
    {
        var editor = new PlateDraftEditor();
        editor.Load(["A1", "A2", "A3", "B1", "B2", "C1"]);
        editor.Select("A1");
        editor.Select("B2", extend: true);
        Assert.Equal(new[] { "A1", "A2", "B1", "B2" }, editor.SelectedWells);
        editor.Select("A2", toggle: true);
        Assert.DoesNotContain("A2", editor.SelectedWells);
        Assert.Equal("A2", editor.Adjacent("A1", 0, 1));
        Assert.Null(editor.Adjacent("A1", -1, 0));
        Assert.Null(editor.Adjacent("B2", 0, 1));
    }

    [Fact]
    public void ExplicitBatchPreservesUncheckedMetadataAndCanUndoAsOneEdit()
    {
        var editor = new PlateDraftEditor();
        editor.Load(["A1", "A2"],
            [new("A1", WellRoleNames.Sample, "Old", "Existing", 1, "BRAF", "keep note"),
             new("A2", WellRoleNames.Sample, "Old", "Existing", 2, "BRAF", "keep note")]);
        editor.SelectAll();
        editor.ApplyBatch(new PlateBatchEdit(WellRoleNames.Sample, ApplySampleId: true, SampleId: "S-001",
            ApplyGroup: true, Group: "Batch A", AssignReplicateSequence: true, FirstReplicate: 3));

        IReadOnlyList<WellAssignmentDto> assignments = editor.ToAssignments();
        Assert.All(assignments, item =>
        {
            Assert.Equal("S-001", item.SampleId);
            Assert.Equal("Batch A", item.Group);
            Assert.Equal("BRAF", item.Target);
            Assert.Equal("keep note", item.Notes);
        });
        Assert.Equal(new int?[] { 3, 4 }, assignments.Select(item => item.ReplicateIndex));
        Assert.True(editor.IsDirty);
        editor.Undo();
        Assert.False(editor.IsDirty);
        Assert.All(editor.Rows, item => Assert.Equal("Old", item.SampleId));
        editor.Redo();
        Assert.True(editor.IsDirty);
        Assert.All(editor.Rows, item => Assert.Equal("S-001", item.SampleId));
        editor.AcceptChanges();
        Assert.False(editor.IsDirty);
        Assert.False(editor.CanUndo);
    }

    [Fact]
    public void ChangingRoleKeepsEditableMetadataButNonSampleSubmissionDoesNotLeakIt()
    {
        var editor = new PlateDraftEditor();
        editor.Load(["A1"], [new("A1", WellRoleNames.Sample, "S-1", "G", 2, "BRAF", "Notes")]);
        editor.SelectAll();
        editor.ApplyBatch(new PlateBatchEdit(WellRoleNames.Ntc));
        Assert.Equal("S-1", Assert.Single(editor.Rows).SampleId);
        WellAssignmentDto submitted = Assert.Single(editor.ToAssignments());
        Assert.Equal(WellRoleNames.Ntc, submitted.Role);
        Assert.Null(submitted.SampleId);
        Assert.Null(submitted.Group);
        Assert.Null(submitted.Target);
        Assert.Null(submitted.Notes);
        editor.Undo();
        Assert.Equal("Notes", Assert.Single(editor.ToAssignments()).Notes);
    }

    [Fact]
    public void InvalidReplicateDraftSurvivesUntilSubmitAndCanBeCorrected()
    {
        var editor = new PlateDraftEditor();
        editor.Load(["A1"]);
        editor.Update(new WellDraft("A1", WellRoleNames.Sample, "S-1", ReplicateIndex: double.NaN));
        Assert.True(editor.IsDirty);
        Assert.Throws<ArgumentException>(() => editor.ToAssignments());
        editor.Update(Assert.Single(editor.Rows) with { ReplicateIndex = 1, Group = "G", Target = "T", Notes = "N" });
        WellAssignmentDto assignment = Assert.Single(editor.ToAssignments());
        Assert.Equal(1, assignment.ReplicateIndex);
        Assert.Equal("G", assignment.Group);
        Assert.Equal("T", assignment.Target);
        Assert.Equal("N", assignment.Notes);
    }

    [Fact]
    public void ApplyingProfileIsOneUndoableEditAndPreservesOtherWellsAndSelection()
    {
        var editor = new PlateDraftEditor();
        editor.Load(["A1", "B2"], [new("B2", WellRoleNames.Sample, "Original", "Group", 2, "Target", "Notes")]);
        editor.Select("B2");
        editor.ApplyAssignments([new("A1", WellRoleNames.Ntc), new("B2", WellRoleNames.Sample, "B2", replicateIndex: 1)]);
        Assert.True(editor.IsDirty);
        Assert.True(editor.CanUndo);
        editor.Undo();
        Assert.False(editor.IsDirty);
        Assert.False(editor.CanUndo);
        Assert.Equal("Original", editor.Rows[1].SampleId);
        Assert.Equal("Notes", editor.Rows[1].Notes);
        Assert.Equal(["B2"], editor.SelectedWells);
        editor.Redo();
        Assert.True(editor.IsDirty);
        Assert.Equal("B2", editor.Rows[1].SampleId);
        editor.ApplyAssignments([new("A1", WellRoleNames.Positive100)]);
        Assert.Equal("B2", editor.Rows[1].SampleId);
        editor.Undo();
        Assert.Equal(WellRoleNames.Ntc, editor.Rows[0].Role);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InvalidProfileBatchDoesNotChangeDraftOrHistory(bool duplicate)
    {
        var editor = new PlateDraftEditor();
        editor.Load(["A1"]);
        editor.ApplyAssignments([new("A1", WellRoleNames.Ntc)]);
        var before = editor.Rows;
        Assert.Throws<ArgumentException>(() => editor.ApplyAssignments(
            [new("A1", WellRoleNames.Positive100), new(duplicate ? "A1" : "A2", WellRoleNames.Ntc)]));
        Assert.Equal(before, editor.Rows);
        editor.Undo();
        Assert.False(editor.IsDirty);
        Assert.False(editor.CanUndo);
    }

    [Fact]
    public void LongSavedMetadataSurvivesUnrelatedEditAndSubmissionWithoutTruncation()
    {
        string group = new('g', 3000);
        string target = new('t', 3001);
        string notes = new('n', 6000);
        var editor = new PlateDraftEditor();
        editor.Load(["A1"], [new("A1", WellRoleNames.Sample, "S-1", group, 1, target, notes)]);

        editor.Update(Assert.Single(editor.Rows) with { ReplicateIndex = 2 });

        WellAssignmentDto result = Assert.Single(editor.ToAssignments());
        Assert.Equal(group, result.Group);
        Assert.Equal(target, result.Target);
        Assert.Equal(notes, result.Notes);
        Assert.Equal(2, result.ReplicateIndex);
    }
}
