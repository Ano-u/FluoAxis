namespace FluoAxis.Infrastructure.Tests;

using System.Globalization;
using System.Text;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using FluoAxis.Contracts;
using FluoAxis.Infrastructure;
using FluoAxis.Presentation;
using Xunit;

[Collection(FilesystemBoundaryGroup.Name)]
public sealed class FixedPlateAutomaticWorkflowTests
{
    [Fact]
    public async Task RecognitionIsReadOnlyAndRejectsSameLengthSourceTampering()
    {
        using var fixture = new Fixture();
        File.WriteAllText(fixture.Source, "Time(min),A1_FAM,A1_ROX\n0,1000,500\n120,1000,500\n", new UTF8Encoding(false));
        await using var store = new PersistentDesktopStore(fixture.Managed);
        CancellationToken token = TestContext.Current.CancellationToken;
        var source = Value(await store.StageAsync(fixture.Source, token));
        var result = Value(await store.RecognizeImportAsync(source, token));
        Assert.True(result.MappingReady);
        Assert.Empty(Value(await store.ListProjectsAsync(token)));
        string managed = Path.Combine(fixture.Managed, source.RelativeResourcePath);
        byte[] before = File.ReadAllBytes(managed);
        Value(await store.RecognizeImportAsync(source, token));
        Assert.Equal(before, File.ReadAllBytes(managed));
        File.WriteAllText(managed, Encoding.UTF8.GetString(before).Replace("1000", "9000", StringComparison.Ordinal), new UTF8Encoding(false));
        var rejected = await store.RecognizeImportAsync(source, token);
        Assert.False(rejected.IsSuccess);
        Assert.Equal("IMPORT_AUTO_SOURCE_CHANGED", Assert.Single(rejected.Errors).Code);
        Assert.Empty(Value(await store.ListProjectsAsync(token)));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task UserCornerProfileRecognizesFileAndRunsOrStopsAtScientificFailure(bool negativeLqc, bool missingNtc)
    {
        using var fixture = new Fixture();
        string[] wells = missingNtc ? ["A1", "H1", "H12", "B2", "G11"] : ["A1", "A12", "H1", "H12", "B2", "G11", "A2"];
        var csv = new StringBuilder("Time(min)," + string.Join(',', wells.Select(well => well + "_FAM")) + "," + string.Join(',', wells.Select(well => well + "_ROX")) + "\n");
        foreach (int minute in new[] { 0, 90, 120 })
        {
            csv.Append(minute.ToString(CultureInfo.InvariantCulture)).Append(',');
            csv.AppendJoin(',', wells.Select(well => well switch { "A1" => "1000", "H12" => negativeLqc ? "280" : "640", "B2" => "640", "G11" => "280", _ => "100" }));
            csv.Append(',').AppendJoin(',', wells.Select(well => well == "A1" ? "500" : "50")).Append('\n');
        }
        File.WriteAllText(fixture.Source, csv.ToString(), new UTF8Encoding(false));
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cancellation.CancelAfter(TimeSpan.FromSeconds(30));
        await using var store = new PersistentDesktopStore(fixture.Managed);
        var profile = Value(await store.SavePlateProfileAsync("用户四角固定方案", UserPlateDefaults.CreateAssignments(), false, true, cancellation.Token));
        var source = Value(await store.StageAsync(fixture.Source, cancellation.Token));
        var timer = Stopwatch.StartNew();
        var proposal = Value(await store.RecognizeImportAsync(source, cancellation.Token));
        double recognitionMs = timer.Elapsed.TotalMilliseconds;
        Assert.True(proposal.MappingReady, string.Join(";", proposal.Issues.Select(issue => issue.Code)));
        Assert.True(proposal.RequiresReview); // Role-free source is resolved only by the user-approved profile.
        var viewModel = new WorkbenchViewModel(store);
        await viewModel.InitializeAsync("Synthetic fixed-corner auto flow", cancellation.Token);
        timer.Restart();
        bool result = await viewModel.AnalyzeAutomaticallyAsync(source, proposal, cancellation.Token, profile: profile);
        double automaticMs = timer.Elapsed.TotalMilliseconds;
        Assert.False(viewModel.IsBusy);
        if (missingNtc)
        {
            Assert.False(result);
            Assert.Equal(RunStateNames.New, viewModel.Run!.DomainState);
            Assert.Equal(0, viewModel.Run.Revision);
            Assert.Empty(viewModel.SampleResults);
            return;
        }
        if (negativeLqc)
        {
            Assert.False(result);
            Assert.Equal(RunStateNames.RunInvalid, viewModel.Run!.DomainState);
            Assert.Equal(StatusNames.Fail, viewModel.ControlQcResult!.Status);
            Assert.Empty(viewModel.SampleResults);
            return;
        }
        Assert.True(result, viewModel.ErrorMessage);
        Assert.Equal(RunStateNames.Analyzed, viewModel.Run!.DomainState);
        Assert.Equal(5, viewModel.Run.Revision);
        Assert.Equal(CallNames.Positive, Assert.Single(viewModel.SampleResults, sample => sample.SampleId == "B2").Call);
        Assert.Equal(CallNames.Negative, Assert.Single(viewModel.SampleResults, sample => sample.SampleId == "G11").Call);
        Assert.DoesNotContain(viewModel.SampleResults, sample => sample.SampleId == "A2");
        var evidence = Value(await store.GetRunEvidenceAsync(new(Guid.NewGuid(), viewModel.Run.RunId), cancellation.Token));
        Assert.Equal(WellRoleNames.Positive100, Assert.Single(evidence.Assignments, assignment => assignment.Well == "A1").Role);
        Assert.Equal(WellRoleNames.Ntc, Assert.Single(evidence.Assignments, assignment => assignment.Well == "A12").Role);
        Assert.Equal(WellRoleNames.Ignore, Assert.Single(evidence.Assignments, assignment => assignment.Well == "A2").Role);
        Assert.Equal(ProvenanceCompletenessNames.StoredComplete, evidence.ProvenanceCompleteness);
        Assert.Equal(3, evidence.ScientificEvidence!.Stages.Count);
        Assert.Equal(source.Sha256, Assert.Single(evidence.Snapshot.SourceFiles).Sha256);
        fixture.WriteMeasurement(new { scenario = "fixed-corner-recognition-to-analysis", iterations = 1, concurrency = 1,
            sourceSha256 = source.Sha256, recognitionMilliseconds = recognitionMs, automaticStageMilliseconds = automaticMs,
            revision = viewModel.Run.Revision, samples = viewModel.SampleResults.Count,
            runtime = Environment.Version.ToString(), os = Environment.OSVersion.Version.ToString(), logicalProcessors = Environment.ProcessorCount,
            slaAsserted = false });
    }

    private static T Value<T>(GatewayResult<T> result)
    {
        Assert.True(result.IsSuccess, string.Join(";", result.Errors.Select(error => error.Code)));
        return result.Value!;
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string approved;
        private readonly string repositoryRoot;
        private readonly string root;
        internal string Managed => Path.Combine(root, "workspace");
        internal string Source => Path.Combine(root, "corners.csv");
        internal Fixture()
        {
            DirectoryInfo? repository = new(AppContext.BaseDirectory);
            while (repository is not null && !File.Exists(Path.Combine(repository.FullName, "FluoAxis.slnx"))) repository = repository.Parent;
            repositoryRoot = repository!.FullName;
            approved = Path.Combine(repositoryRoot, "artifacts", "test-tmp");
            root = Path.Combine(approved, nameof(FixedPlateAutomaticWorkflowTests), Environment.ProcessId.ToString(CultureInfo.InvariantCulture), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
        }
        internal void WriteMeasurement(object data)
        {
            string folder = Path.Combine(repositoryRoot, "artifacts", "benchmarks", nameof(FixedPlateAutomaticWorkflowTests), Environment.ProcessId.ToString(CultureInfo.InvariantCulture), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "measurement.json"), JsonSerializer.Serialize(data));
            TestContext.Current.TestOutputHelper?.WriteLine(Path.GetRelativePath(repositoryRoot, Path.Combine(folder, "measurement.json")));
        }
        public void Dispose()
        {
            if (!Path.GetFullPath(root).StartsWith(Path.GetFullPath(approved) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Unsafe test cleanup.");
            Directory.Delete(root, true);
        }
    }
}
