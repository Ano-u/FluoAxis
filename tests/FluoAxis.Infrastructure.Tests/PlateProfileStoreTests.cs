namespace FluoAxis.Infrastructure.Tests;

using System.Globalization;
using System.Text.Json;
using FluoAxis.Contracts;
using FluoAxis.Infrastructure;
using Xunit;

[Collection(FilesystemBoundaryGroup.Name)]
public sealed class PlateProfileStoreTests
{
    private static WellAssignmentDto[] Assignments() => [new("A1", WellRoleNames.Ntc), new("A2", WellRoleNames.Positive100),
        new("A3", WellRoleNames.WildType), new("A4", WellRoleNames.Lqc01Percent),
        new("A5", WellRoleNames.Sample, "OLD-SAMPLE-SECRET", "OLD-GROUP", 7, "OLD-TARGET", "OLD-NOTES")];

    [Fact]
    public async Task Profiles_preserve_options_and_default_across_restart_without_persisting_old_sample_identity()
    {
        using var fixture = new Fixture();
        Guid first;
        Guid second;
        var options = new ImportOptionsDto("gb18030", "tab", "invariant", "seconds");
        await using (var store = new PersistentDesktopStore(fixture.Root))
        {
            Assert.Empty(Value(await store.ListPlateProfilesAsync(TestContext.Current.CancellationToken)));
            PlateProfileDto saved = Value(await store.SavePlateProfileAsync("固定布局", Assignments(), true, true, TestContext.Current.CancellationToken, options));
            first = saved.ProfileId; Assert.True(saved.IsDefault);
            Assert.Equal(options, saved.Definition.Options); Assert.Equal(5, saved.Definition.Wells.Count);
            string json = JsonSerializer.Serialize(saved, PlateProfileJsonContext.Default.PlateProfileDto);
            Assert.DoesNotContain("OLD-", json, StringComparison.Ordinal);
            second = Value(await store.SavePlateProfileAsync("调整后的布局", Assignments(), false, false, TestContext.Current.CancellationToken)).ProfileId;
            Assert.NotEqual(first, second);
            Assert.True(Value(await store.SetDefaultPlateProfileAsync(second, TestContext.Current.CancellationToken)).IsDefault);
        }
        await using var reopened = new PersistentDesktopStore(fixture.Root);
        IReadOnlyList<PlateProfileDto> profiles = Value(await reopened.ListPlateProfilesAsync(TestContext.Current.CancellationToken));
        Assert.Equal(2, profiles.Count); Assert.Equal(second, Assert.Single(profiles, profile => profile.IsDefault).ProfileId);
        PlateProfileDto old = Value(await reopened.GetPlateProfileAsync(first, TestContext.Current.CancellationToken))!;
        Assert.Equal(options, old.Definition.Options); Assert.False(old.IsDefault);
        Assert.Null(Value(await reopened.GetPlateProfileAsync(Guid.NewGuid(), TestContext.Current.CancellationToken)));
    }

    [Fact]
    public async Task Corrupted_profile_is_not_offered_for_automatic_reuse()
    {
        using var fixture = new Fixture();
        await using var store = new PersistentDesktopStore(fixture.Root);
        PlateProfileDto saved = Value(await store.SavePlateProfileAsync("Original", Assignments(), true, true, TestContext.Current.CancellationToken));
        string entry = Path.Combine(fixture.Root, "profiles", $"profile-{saved.ProfileId:N}.json");
        File.WriteAllText(entry, File.ReadAllText(entry).Replace("Original", "Tampered", StringComparison.Ordinal));
        GatewayResult<PlateProfileDto?> result = await store.GetPlateProfileAsync(saved.ProfileId, TestContext.Current.CancellationToken);
        Assert.False(result.IsSuccess); Assert.Equal("PLATE_PROFILE_INVALID", result.Errors[0].Code);
        Assert.False((await store.ListPlateProfilesAsync(TestContext.Current.CancellationToken)).IsSuccess);
    }

    [Fact]
    public async Task Incomplete_or_duplicate_controls_and_excess_profiles_fail_closed()
    {
        using var fixture = new Fixture();
        await using var store = new PersistentDesktopStore(fixture.Root);
        Assert.False((await store.SavePlateProfileAsync("Incomplete", Assignments()[1..], true, true, TestContext.Current.CancellationToken)).IsSuccess);
        Assert.False((await store.SavePlateProfileAsync("Duplicate", [.. Assignments(), Assignments()[0]], true, true, TestContext.Current.CancellationToken)).IsSuccess);
        string folder = Path.Combine(fixture.Root, "profiles"); Directory.CreateDirectory(folder);
        for (int index = 0; index < 100; index++) File.WriteAllText(Path.Combine(folder, $"profile-{Guid.NewGuid():N}.json"), "{}");
        GatewayResult<PlateProfileDto> exceeded = await store.SavePlateProfileAsync("Excess", Assignments(), true, false, TestContext.Current.CancellationToken);
        Assert.Equal("PLATE_PROFILE_LIMIT", exceeded.Errors[0].Code);
        Assert.Equal(100, Directory.EnumerateFiles(folder, "profile-*.json").Count());
    }

    [Fact]
    public async Task Pre_canceled_save_does_not_create_a_profile_or_default_reference()
    {
        using var fixture = new Fixture();
        await using var store = new PersistentDesktopStore(fixture.Root);
        await store.Ready;
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.SavePlateProfileAsync("Cancelled", Assignments(), true, true, cancellation.Token));
        Assert.Empty(Value(await store.ListPlateProfilesAsync(TestContext.Current.CancellationToken)));
    }

    private static T Value<T>(GatewayResult<T> result)
    { Assert.True(result.IsSuccess, string.Join(",", result.Errors.Select(error => error.Code))); return result.Value!; }
    private sealed class Fixture : IDisposable
    {
        private readonly string approved;
        internal string Root { get; }
        internal Fixture()
        {
            DirectoryInfo? directory = new(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "FluoAxis.slnx"))) directory = directory.Parent;
            approved = Path.Combine(directory?.FullName ?? throw new InvalidOperationException("Repository not found."), "artifacts", "test-tmp");
            Root = Path.Combine(approved, nameof(PlateProfileStoreTests), Environment.ProcessId.ToString(CultureInfo.InvariantCulture), Guid.NewGuid().ToString("N"));
        }
        public void Dispose()
        {
            if (!Path.GetFullPath(Root).StartsWith(Path.GetFullPath(approved) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Unsafe fixture cleanup.");
            if (Directory.Exists(Root)) Directory.Delete(Root, true);
        }
    }
}
