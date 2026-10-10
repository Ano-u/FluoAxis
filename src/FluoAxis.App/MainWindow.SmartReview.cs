#if DEBUG
namespace FluoAxis.App;

using FluoAxis.Contracts;
using FluoAxis.Infrastructure;
using Microsoft.UI.Xaml;

public sealed partial class MainWindow
{
    internal async Task VerifySmartRefinementAsync(string relativeSources, string relativeOutput)
    {
        string phase = "initialization";
        var timer = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            await initializationTask.WaitAsync(TimeSpan.FromSeconds(30));
            await windowActivated.Task.WaitAsync(TimeSpan.FromSeconds(30));
            phase = "automatic";
            await NativeVisualEvidence.CompleteAsync(relativeOutput, "RUNNING: " + phase);
            var originalSource = await StageAsync("valid.csv");
            await StartSmartImportAsync(true, reusedSource: originalSource);
            RequireAnalyzed();
            Guid originalRun = viewModel.Run!.RunId;
            WellEditor detachedEditor = wellEditors["B2"];

            phase = "refine-new-run";
            await NativeVisualEvidence.CompleteAsync(relativeOutput, "RUNNING: " + phase);
            OnRefineSmartClick(RefineSmartButton, new RoutedEventArgs());
            await activeOperationTask;
            Require(viewModel.Run?.RunId != originalRun && viewModel.Run?.DomainState == RunStateNames.New);
            await ContinueAsync();
            RequireAnalyzed();
            detachedEditor.Sample.Text = "Stale-Old-Run";
            await Task.Delay(50); // Let WinUI deliver the detached TextChanged event.
            Require(!plateDraft.IsDirty && plateDraft.Rows.Single(row => row.Well == "B2").SampleId == "B2");
            var preserved = await workspace.Gateway.GetRunAsync(new(Guid.NewGuid(), originalRun), workbenchLifetime.Token);
            Require(preserved.IsSuccess && preserved.Value!.Revision == 5 && preserved.Value.DomainState == RunStateNames.Analyzed);

            phase = "preview-options";
            await NativeVisualEvidence.CompleteAsync(relativeOutput, "RUNNING: " + phase);
            RestoreImportOptions(new ImportOptionsDto(timeUnit: "seconds"));
            await StartSmartImportAsync(false, reusedSource: await StageAsync("seconds.csv"));
            Require(ReadImportOptions().TimeUnit == "seconds");
            await ContinueAsync();
            RequireAnalyzed();
            Require(viewModel.CurrentEvidence?.Mapping?.Options?.TimeUnit == "seconds");

            phase = "partial-metadata-stop";
            await NativeVisualEvidence.CompleteAsync(relativeOutput, "RUNNING: " + phase);
            await StartSmartImportAsync(true, reusedSource: await StageAsync("partial.csv"));
            Require(viewModel.Run?.DomainState == RunStateNames.New && smartProposal?.Issues.Any(item => item.Code == "IMPORT_AUTO_SAMPLE_METADATA_REQUIRED") == true);
            await ContinueAsync();
            Require(viewModel.Run?.DomainState == RunStateNames.Imported && activeStage == "plate");

            phase = "profile-undo";
            OnApplyPlateProfileClick(ApplyPlateProfileButton, new RoutedEventArgs());
            Require(plateDraft.IsDirty && plateDraft.CanUndo);
            OnUndoPlateClick(UndoPlateButton, new RoutedEventArgs());
            Require(!plateDraft.IsDirty && plateDraft.Rows.All(row => row.Role is null));
            OnRedoPlateClick(RedoPlateButton, new RoutedEventArgs());
            Require(plateDraft.IsDirty && plateDraft.Rows.All(row => row.Role is not null));
            phase = "manual-sample-edit";
            RootSurface.UpdateLayout();
            wellEditors["B2"].Sample.StartBringIntoView();
            await Task.Delay(150);
            wellEditors["B2"].Sample.Text = "Reviewed-B2";
            await Task.Delay(50); // Match a user editing before the next button event.
            Require(plateDraft.Rows.Single(row => row.Well == "B2").SampleId == "Reviewed-B2");
            await ContinueAsync();
            RequireAnalyzed();
            Require(viewModel.SampleResults.Any(item => item.SampleId == "Reviewed-B2"));

            phase = "explicit-file-identities";
            SmartProfileChooser.SelectedIndex = 0;
            await StartSmartImportAsync(false, reusedSource: await StageAsync("explicit.csv"));
            await ContinueAsync();
            RequireAnalyzed();
            Require(viewModel.SampleResults.Any(item => item.SampleId == "File-B2"));
            Require(!GlobalInfoBar.IsOpen);

            phase = "capture";
            RootSurface.RequestedTheme = ElementTheme.Light;
            double scale = RootSurface.XamlRoot.RasterizationScale;
            AppWindow.Resize(new Windows.Graphics.SizeInt32((int)(1280 * scale), (int)(980 * scale)));
            RootSurface.UpdateLayout();
            await Task.Delay(200);
            Require(StageContentScroll.VerticalOffset < 1 && OperationText.Text.StartsWith("已完成", StringComparison.Ordinal));
            await NativeVisualEvidence.CaptureAsync(RootSurface, relativeOutput, "smart-refinement.png");
            await NativeVisualEvidence.CompleteAsync(relativeOutput, $"PASS: automatic and manual continuation, preserved original Run, seconds options, metadata stop, profile undo/redo, edited and file sample identities. elapsedMs={timer.Elapsed.TotalMilliseconds:F1}. Test adapter supplies sources; no picker interaction.");
        }
        catch (Exception)
        {
            string draftSample = plateDraft.Rows.FirstOrDefault(row => row.Well == "B2")?.SampleId ?? "absent";
            string editorSample = wellEditors.GetValueOrDefault("B2")?.Sample.Text ?? "absent";
            Environment.ExitCode = 1;
            await NativeVisualEvidence.CompleteAsync(relativeOutput, $"REFINEMENT_FAILED: {phase} · {viewModel.RunState} · {viewModel.ErrorCode ?? "UI"} · updating={updatingDraftControls} · dirty={plateDraft.IsDirty} · fieldSynchronized={draftSample == editorSample} · loaded={wellEditors.GetValueOrDefault("B2")?.Sample.IsLoaded}");
        }
        finally { await workspace.CloseAsync(); closeAllowed = true; Close(); }

        async Task<SourceFileDto> StageAsync(string name)
        {
            var result = await workspace.StageValidationSourceAsync(relativeSources.TrimEnd('/') + "/" + name);
            Require(result.IsSuccess);
            return result.Value!;
        }
        async Task ContinueAsync()
        {
            Require(ContinueSmartButton.IsEnabled);
            OnContinueSmartClick(ContinueSmartButton, new RoutedEventArgs());
            await activeOperationTask;
        }
        void RequireAnalyzed() => Require(viewModel.Run?.DomainState == RunStateNames.Analyzed && viewModel.Run.Revision == 5 && viewModel.SampleResults.Count == 2);
        static void Require(bool condition) { if (!condition) throw new InvalidOperationException("Native refinement expectation failed."); }
    }

    internal async Task VerifySmartWorkflowAsync(string relativeSource, string relativeOutput)
    {
        try
        {
            await initializationTask.WaitAsync(TimeSpan.FromSeconds(30));
            await windowActivated.Task.WaitAsync(TimeSpan.FromSeconds(30));
            var staged = await workspace.StageValidationSourceAsync(relativeSource);
            if (!staged.IsSuccess) throw new InvalidOperationException("SMART_REVIEW_SOURCE_FAILED");
            await StartSmartImportAsync(true, reusedSource: staged.Value!);
            if (viewModel.Run?.DomainState != RunStateNames.Analyzed || viewModel.Run.Revision != 5 || viewModel.SampleResults.Count != 2)
                throw new InvalidOperationException("SMART_REVIEW_ANALYSIS_FAILED");
            if (SelectedPlateProfile is not { } profile || profile.Definition.Wells.Count(item => item.Role == WellRoleNames.Sample) != 60)
                throw new InvalidOperationException("SMART_REVIEW_PROFILE_FAILED");
            RootSurface.RequestedTheme = ElementTheme.Light;
            double scale = RootSurface.XamlRoot.RasterizationScale;
            AppWindow.Resize(new Windows.Graphics.SizeInt32((int)(1280 * scale), (int)(980 * scale)));
            RootSurface.UpdateLayout();
            await Task.Delay(200);
            await NativeVisualEvidence.CaptureAsync(RootSurface, relativeOutput, "smart-analysis.png");
            await NativeVisualEvidence.CompleteAsync(relativeOutput, "PASS: native default-profile selection, managed source recognition, automatic revision 0 to 5, two saved samples. Source supplied by test adapter; native picker not tested.");
        }
        catch (Exception)
        {
            Environment.ExitCode = 1;
            await NativeVisualEvidence.CompleteAsync(relativeOutput, $"SMART_REVIEW_FAILED · {viewModel.RunState} · {viewModel.ErrorCode ?? "UI"}");
        }
        finally { await workspace.CloseAsync(); closeAllowed = true; Close(); }
    }
}
#endif
