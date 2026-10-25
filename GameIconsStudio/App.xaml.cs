using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;

namespace GameIconsStudio;

public partial class App : Application
{
    private Window? _window;

    public App()
    {
        this.InitializeComponent();

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
        }
        catch (Exception ex)
        {
            LogCrash("OnLaunched", ex);
            throw;
        }
    }
}
