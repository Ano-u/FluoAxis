namespace FluoAxis.App;

using FluoAxis.Presentation;
using Microsoft.UI.Xaml;

public partial class App : Application
{
    private Window? window;

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        string launchArguments = string.Join(' ', Environment.GetCommandLineArgs().Skip(1));
        var gateway = new FakeFluoAxisGateway(new FakeMethodIdentity(
            "BRAF-V600E-Research",
            "1.0",
            new string('0', 64)));
        window = new MainWindow(new WorkbenchViewModel(gateway), launchArguments);
        window.Activate();
    }
}
