using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using WinRT.Interop;

namespace GameIconsStudio;

public partial class App : Application
{
    private Window? _window;
    private bool _bringToFrontOnLaunch;

    public App()
    {
        this.InitializeComponent();

        // Single-instance: a second process redirects its activation here.
        AppInstance.GetCurrent().Activated += OnInstanceActivated;

        // Global crash logging -> crash_log.txt next to the .exe
        this.UnhandledException += (s, e) =>
            LogCrash("App.UnhandledException", e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            LogCrash("AppDomain.UnhandledException", e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (s, e) =>
        {
            LogCrash("TaskScheduler.UnobservedTaskException", e.Exception);
            e.SetObserved();
        };
    }

    private static void LogCrash(string source, Exception? ex)
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "crash_log.txt");
            var text =
                $"===== {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{source}] ====={Environment.NewLine}" +
                $"{ex}{Environment.NewLine}{Environment.NewLine}";
            File.AppendAllText(path, text);
        }
        catch
        {
            // last resort: never throw from the crash logger
        }
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            _window = new MainWindow();
            _window.Activate();
            if (_bringToFrontOnLaunch) BringToFront(_window);
        }
        catch (Exception ex)
        {
            LogCrash("OnLaunched", ex);
            throw;
        }
    }

    /// <summary>
    /// Fires (on a background thread) when a second launch redirects here —
    /// marshal to the UI thread and pull the existing window to the front.
    /// </summary>
    private void OnInstanceActivated(object? sender, AppActivationArguments args)
    {
        if (_window is { } w)
            w.DispatcherQueue.TryEnqueue(() => BringToFront(w));
        else
            _bringToFrontOnLaunch = true;
    }

    private static void BringToFront(Window window)
    {
        var hwnd = WindowNative.GetWindowHandle(window);
        const int SW_RESTORE = 9;
        if (IsIconic(hwnd))
            ShowWindow(hwnd, SW_RESTORE);
        window.Activate();
        SetForegroundWindow(hwnd);
    }

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);
}
