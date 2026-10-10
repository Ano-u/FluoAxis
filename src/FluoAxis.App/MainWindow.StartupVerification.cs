namespace FluoAxis.App;

using FluoAxis.Presentation;
using Microsoft.UI.Xaml;

public sealed partial class MainWindow
{
    internal async Task<string> VerifyStartupAsync()
    {
        var loaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnLoaded(object sender, RoutedEventArgs args) => loaded.TrySetResult();
        RootSurface.Loaded += OnLoaded;
        try
        {
            if (RootSurface.IsLoaded) loaded.TrySetResult();
            await Task.WhenAll(initializationTask, loaded.Task).WaitAsync(TimeSpan.FromSeconds(30));
            if (!workspaceAvailable || viewModel.HasError || viewModel.UiState is not (WorkbenchUiState.Ready or WorkbenchUiState.Empty))
                throw new InvalidOperationException("SYSTEM_STARTUP_WORKBENCH_NOT_READY");
            RootSurface.UpdateLayout();
            if (RootSurface.ActualWidth <= 0 || StageWorkspace.ActualWidth <= 0 || PrimaryStageAction.Content is null)
                throw new InvalidOperationException("SYSTEM_STARTUP_LAYOUT_NOT_READY");
            return viewModel.Run is null ? "empty" : "ready";
        }
        finally { RootSurface.Loaded -= OnLoaded; }
    }

    internal async Task PrepareVerifiedShutdownAsync()
    {
        await workspace.CloseAsync();
        closeAllowed = true;
    }
}
