using System.Windows;
using LilAgents.UI;

namespace LilAgents;

public partial class App : Application
{
    private const string SingleInstanceMutexName = "Global\\lil-agents-single-instance";

    private Mutex? _singleInstanceMutex;
    private AgentsController? _controller;
    private TrayMenu? _tray;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Two copies would fight over window placement and double every sound.
        _singleInstanceMutex = new Mutex(true, SingleInstanceMutexName, out var isFirstInstance);
        if (!isFirstInstance)
        {
            Shutdown();
            return;
        }

        DispatcherUnhandledException += (_, args) =>
        {
            // A failure inside one frame of the render loop should not take the app down;
            // the characters simply skip that tick.
            System.Diagnostics.Debug.WriteLine(args.Exception);
            args.Handled = true;
        };

        _controller = new AgentsController();
        _controller.Start();

        _tray = new TrayMenu(_controller);
        _tray.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        _controller?.Dispose();
        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }
}
