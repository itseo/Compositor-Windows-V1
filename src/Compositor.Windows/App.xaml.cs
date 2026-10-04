using Microsoft.UI.Xaml;

namespace Compositor.Windows;

public partial class App : Application
{
    private Window? _window;

    public App()
    {
        StartupLog.Write("App constructor entered.");
        UnhandledException += OnUnhandledException;
        InitializeComponent();
        StartupLog.Write("App InitializeComponent completed.");
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            StartupLog.Write("OnLaunched entered.");
            _window = new MainWindow();
            StartupLog.Write("MainWindow created.");
            _window.Activate();
            StartupLog.Write("MainWindow activated.");
        }
        catch (Exception ex)
        {
            StartupLog.Write("Fatal exception in OnLaunched: " + ex);
            throw;
        }
    }

    private static void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        StartupLog.Write("WinUI unhandled exception: " + e.Exception);
    }
}

internal static class StartupLog
{
    private static readonly string LogDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CompositorWindows");

    private static readonly string LogPath = Path.Combine(LogDirectory, "startup.log");

    public static void Write(string message)
    {
        try
        {
            Directory.CreateDirectory(LogDirectory);
            File.AppendAllText(
                LogPath,
                $"[{DateTimeOffset.Now:O}] {message}{Environment.NewLine}");
        }
        catch
        {
            // Diagnostics must never become a second startup failure.
        }
    }
}
