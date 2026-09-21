namespace FluoAxis.Domain.Tests;

using Xunit;

public class WellAssignmentTests
{
    [Fact]
    public void Constructor_accepts_sample_role_with_sample_metadata()
    {
        var sample = new SampleAssignment("S-001", "G1", 1, "BRAF", "first replicate");

        var assignment = new WellAssignment(new WellId('A', 1), WellRole.Sample, sample);

        Assert.Equal(new WellId('A', 1), assignment.Well);
        Assert.Equal(WellRole.Sample, assignment.Role);
        Assert.Same(sample, assignment.Sample);
    }

    [Fact]
    public void Constructor_accepts_control_roles_with_null_sample()
    {
        foreach ((WellRole role, WellId well) in new[]
        {
            (WellRole.Ntc, new WellId('A', 2)),
            (WellRole.Positive100, new WellId('A', 3)),
            (WellRole.WildType, new WellId('A', 4)),
            (WellRole.Lqc01Percent, new WellId('A', 5)),
            (WellRole.Ignore, new WellId('A', 6)),
        })
        {
            var assignment = new WellAssignment(well, role, null);

            Assert.Equal(role, assignment.Role);
            Assert.Null(assignment.Sample);
        }
    }

    [Fact]
    public void Constructor_rejects_sample_metadata_on_control_roles()
    {
        var sample = new SampleAssignment("S-001", null, 1, null, null);

        Assert.Throws<ArgumentException>(() => new WellAssignment(new WellId('A', 2), WellRole.Ntc, sample));
        Assert.Throws<ArgumentException>(() => new WellAssignment(new WellId('A', 3), WellRole.Positive100, sample));
    }

    [Fact]
    public void SampleAssignment_rejects_blank_id_and_non_positive_replicate_index()
    {
        Assert.Throws<ArgumentException>(() => new SampleAssignment("", null, 1, null, null));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SampleAssignment("S-001", null, 0, null, null));
    }

    [Fact]
    public void SampleAssignment_keeps_optional_metadata()
    {
        var sample = new SampleAssignment("S-001", "group-a", 2, "BRAF", "note");

        Assert.Equal("S-001", sample.SampleId);
        Assert.Equal("group-a", sample.Group);
        Assert.Equal(2, sample.ReplicateIndex);
        Assert.Equal("BRAF", sample.Target);
        Assert.Equal("note", sample.Notes);
    }
}
