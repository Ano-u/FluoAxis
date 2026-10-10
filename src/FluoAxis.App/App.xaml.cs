namespace FluoAxis.App;

using FluoAxis.Infrastructure;
using FluoAxis.Presentation;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

public partial class App : Application
{
    private Window? window;

    public App()
    {
        InitializeComponent();
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        string[] launchArgumentsList = Environment.GetCommandLineArgs();
        DesktopStartupVerification? startupVerification;
        try { startupVerification = DesktopStartupVerification.FromArguments(launchArgumentsList); }
        catch (Exception)
        {
            Environment.ExitCode = 2;
            Exit();
            return;
        }
#if DEBUG
        string[] processArguments = Environment.GetCommandLineArgs();
        if (processArguments.Length == 3 && processArguments[1] == "--native-dpi-smoke")
        {
            await NativeDpiSmoke.RunAsync(processArguments[2]);
            Exit();
            return;
        }
        if (processArguments.Length == 5 && processArguments[1] is "--native-smart-smoke" or "--native-smart-refinement" or "--native-plot-check")
        {
            UnhandledException += async (_, failure) =>
            {
                failure.Handled = true;
                string methods = string.Join(" > ", new System.Diagnostics.StackTrace(failure.Exception, false).GetFrames()
                    .Select(frame => frame.GetMethod()?.Name).Take(10));
                await NativeVisualEvidence.CompleteAsync(processArguments[4], $"NATIVE_SMART_UNHANDLED: {failure.Exception.GetType().Name} {failure.Exception.HResult:X8} · {methods}");
                Environment.ExitCode = 1;
                Exit();
            };
            await using var verificationWorkspace = DesktopWorkspace.CreateValidationWorkspace(processArguments[2]);
            var verificationWindow = new MainWindow(new WorkbenchViewModel(verificationWorkspace.Gateway), verificationWorkspace);
            window = verificationWindow;
            verificationWindow.Activate();
            if (processArguments[1] == "--native-plot-check")
                await verificationWindow.VerifyNativePlotAsync(processArguments[3], processArguments[4]);
            else if (processArguments[1] == "--native-smart-refinement")
                await verificationWindow.VerifySmartRefinementAsync(processArguments[3], processArguments[4]);
            else await verificationWindow.VerifySmartWorkflowAsync(processArguments[3], processArguments[4]);
            Exit();
            return;
        }
        if (processArguments.Length == 5 && processArguments[1] == "--native-layout-smoke")
        {
          string reviewPhase = "WORKSPACE";
          try
          {
            await using var validationWorkspace = DesktopWorkspace.CreateValidationWorkspace(processArguments[2]);
            reviewPhase = "SEED";
            await validationWorkspace.SeedValidationPackageAsync(processArguments[3]);
            reviewPhase = "WINDOW";
            var reviewWindow = new MainWindow(new WorkbenchViewModel(validationWorkspace.Gateway), validationWorkspace);
            reviewPhase = "ACTIVATE";
            window = reviewWindow;
            reviewWindow.Activate();
            await reviewWindow.CaptureVisualReviewAsync(processArguments[4]);
          }
          catch (Exception error)
          {
            string methods = string.Join(" > ", new System.Diagnostics.StackTrace(error, false).GetFrames()
                .Select(frame => frame.GetMethod()?.Name).Take(8));
            await NativeVisualEvidence.CompleteAsync(processArguments[4], $"UI_LAYOUT_{reviewPhase}_FAILED_{error.HResult:X8}\n{methods}");
          }
            Exit();
            return;
        }
        if (processArguments.Length == 3 && processArguments[1] == "--native-report-smoke")
        {
            await ReportRenderingSmoke.RunAsync(processArguments[2]);
            Exit();
            return;
        }
#endif
        string launchArguments = string.Join(' ', Environment.GetCommandLineArgs().Skip(1));
        DesktopWorkspace? workspace = null;
        string startupStage = "workspace";
        try
        {
#if DEBUG
            workspace = startupVerification?.OpenWorkspace() ?? (processArguments.Length is 3 or 4 && processArguments[1] == "--native-workbench-smoke"
                ? DesktopWorkspace.CreateValidationWorkspace(processArguments[2]) : new DesktopWorkspace());
            if (processArguments.Length == 4 && processArguments[1] == "--native-workbench-smoke")
                await workspace.SeedValidationPackageAsync(processArguments[3]);
#else
            workspace = startupVerification?.OpenWorkspace() ?? new DesktopWorkspace();
#endif
            startupStage = "window";
            var mainWindow = new MainWindow(new WorkbenchViewModel(workspace.Gateway), workspace, launchArguments);
            window = mainWindow;
            startupStage = "activate";
            window.Activate();
            if (startupVerification is { IsInteractive: false })
            {
                startupStage = "ready";
                string state = await mainWindow.VerifyStartupAsync();
                await mainWindow.PrepareVerifiedShutdownAsync();
                await startupVerification.CompleteAsync("pass", state);
                mainWindow.Close();
                Exit();
            }
        }
        catch (Exception error)
        {
            StartupFailure failure = StartupFailure.FromException(error, startupStage);
            if (workspace is not null)
            {
                try { await workspace.CloseAsync(); }
                catch (Exception) { /* Keep the original bounded startup diagnosis. */ }
            }
            if (startupVerification is not null)
            {
                await startupVerification.CompleteAsync("fail", "error", failure);
                Environment.ExitCode = 1;
                window?.Close();
                Exit();
                return;
            }
            window = new Window
            {
                Title = "FluoAxis",
                Content = new TextBlock
                {
                    Text = $"{(failure.Stage == "workspace" ? "无法打开本地工作区，请检查访问权限或关闭其他 FluoAxis 实例后重试。" : "主界面初始化失败。请使用完整发布目录启动应用；这条错误不表示实验数据损坏。")}" +
                        $"\n{failure.Code} · {failure.HResult} · {failure.Operation}\n已有数据不会被覆盖。",
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(24),
                },
            };
            window.Activate();
        }
    }
}
