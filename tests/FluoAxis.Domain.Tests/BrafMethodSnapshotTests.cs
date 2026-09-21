namespace FluoAxis.Domain.Tests;

using Xunit;

public class BrafMethodSnapshotTests
{
    [Fact]
    public void Default_carries_the_V1_0_method_specification_values()
    {
        var snapshot = BrafMethodSnapshot.Default;

        Assert.Equal("BRAF-V600E-Research", snapshot.MethodId);
        Assert.Equal("1.0", snapshot.Version);
        Assert.Equal(120.0, snapshot.EndpointMinutes);
        Assert.Equal(15.0, snapshot.EndpointToleranceSeconds);

        Assert.Equal(90.0, snapshot.Plateau.StartMinutes);
        Assert.Equal(120.0, snapshot.Plateau.EndMinutes);
        Assert.Equal(0.01, snapshot.Plateau.RelativeRangeMax);
        Assert.Equal(2, snapshot.Plateau.MinWindowPoints);

        Assert.Equal(-0.05, snapshot.Ntc.NormalizedMin);
        Assert.Equal(0.05, snapshot.Ntc.NormalizedMax);

        Assert.Equal(0.05, snapshot.Wt.FamMeanMaxExclusive);
        Assert.Equal(0.05, snapshot.Wt.RoxMeanMaxExclusive);

        Assert.Equal(0.4, snapshot.Classification.DrPrimeThresholdInclusive);
        Assert.Equal("BRAF V600E 阳性", snapshot.Classification.PositiveLabel);
        Assert.Equal("BRAF V600E 阴性", snapshot.Classification.NegativeLabel);

        Assert.True(snapshot.Replicates.RequireClassificationConsistency);

        Assert.Equal(1, snapshot.Roles.NtcMinimumWells);
        Assert.Equal(1, snapshot.Roles.Positive100MinimumWells);
        Assert.Equal(1, snapshot.Roles.WildTypeMinimumWells);
        Assert.Equal(1, snapshot.Roles.Lqc01PercentMinimumWells);
    }

    [Fact]
    public void CanonicalHash_is_stable_across_calls_and_instances()
    {
        var first = BrafMethodSnapshot.Default.ComputeCanonicalHash();
        var second = new BrafMethodSnapshot().ComputeCanonicalHash();
        var third = BrafMethodSnapshot.Default.ComputeCanonicalHash();

        Assert.Equal(first, second);
        Assert.Equal(first, third);
        Assert.Equal(64, first.Length);
    }

    [Fact]
    public void CanonicalHash_changes_when_any_field_changes()
    {
        var baseline = BrafMethodSnapshot.Default.ComputeCanonicalHash();

        var changedThreshold = BrafMethodSnapshot.Default with
        {
            Classification = new ClassificationSpec(drPrimeThresholdInclusive: 0.5),
        };
        var changedVersion = BrafMethodSnapshot.Default with { Version = "1.1" };
        var changedWindow = BrafMethodSnapshot.Default with
        {
            Plateau = new PlateauSpec(startMinutes: 80.0),
        };
        var changedRoles = BrafMethodSnapshot.Default with
        {
            Roles = new RoleCardinalitySpec(ntcMinimumWells: 2),
        };

        Assert.NotEqual(baseline, changedThreshold.ComputeCanonicalHash());
        Assert.NotEqual(baseline, changedVersion.ComputeCanonicalHash());
        Assert.NotEqual(baseline, changedWindow.ComputeCanonicalHash());
        Assert.NotEqual(baseline, changedRoles.ComputeCanonicalHash());
    }

    [Fact]
    public void Default_is_a_shared_immutable_instance()
    {
        Assert.Same(BrafMethodSnapshot.Default, BrafMethodSnapshot.Default);
    }

    [Fact]
    public void Constructor_rejects_non_positive_endpoint()
    {
        Assert.Throws<ArgumentException>(() => new BrafMethodSnapshot(endpointMinutes: 0));
        Assert.Throws<ArgumentException>(() => new BrafMethodSnapshot(endpointMinutes: -10));
        Assert.Throws<ArgumentException>(() => new BrafMethodSnapshot(endpointMinutes: double.NaN));
    }

    [Fact]
    public void Constructor_rejects_negative_tolerance()
    {
        Assert.Throws<ArgumentException>(() => new BrafMethodSnapshot(endpointToleranceSeconds: -1));
    }

    [Fact]
    public void Constructor_rejects_reversed_plateau_window()
    {
        Assert.Throws<ArgumentException>(() => new BrafMethodSnapshot(plateau: new PlateauSpec(startMinutes: 120.0, endMinutes: 90.0)));
    }

    [Fact]
    public void Constructor_rejects_plateau_window_beyond_endpoint()
    {
        Assert.Throws<ArgumentException>(() => new BrafMethodSnapshot(
            endpointMinutes: 100.0,
            plateau: new PlateauSpec(startMinutes: 90.0, endMinutes: 120.0)));
    }

    [Fact]
    public void Constructor_rejects_invalid_sub_specs()
    {
        Assert.Throws<ArgumentException>(() => new BrafMethodSnapshot(
            plateau: new PlateauSpec(relativeRangeMax: -0.01)));
        Assert.Throws<ArgumentException>(() => new BrafMethodSnapshot(
            plateau: new PlateauSpec(minWindowPoints: 0)));
        Assert.Throws<ArgumentException>(() => new BrafMethodSnapshot(
            ntc: new NtcSpec(normalizedMin: 0.05, normalizedMax: -0.05)));
        Assert.Throws<ArgumentException>(() => new BrafMethodSnapshot(
            classification: new ClassificationSpec(drPrimeThresholdInclusive: -0.4)));
        Assert.Throws<ArgumentException>(() => new BrafMethodSnapshot(
            roles: new RoleCardinalitySpec(ntcMinimumWells: -1)));
    }
}
