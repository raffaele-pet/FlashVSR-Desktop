using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using FlashVsr.Desktop.Services;
using FlashVsr.Desktop.Views;

namespace FlashVsr.Desktop;
public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += (_, error) => {
            Directory.CreateDirectory(AppPaths.Data);
            File.AppendAllText(Path.Combine(AppPaths.Data, "crash.log"), error.Exception + Environment.NewLine);
            MessageBox.Show(error.Exception.Message, "FlashVSR Desktop", MessageBoxButton.OK, MessageBoxImage.Error);
            error.Handled = true;
        };
        // The headless installer uses the exact same service as the GUI. It
        // is intended for deployment/testing; normal users double-click Setup.
        if (e.Args.Contains("--install-headless"))
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            string Arg(string name, string fallback = "") { var i = Array.IndexOf(e.Args, name); return i >= 0 && i + 1 < e.Args.Length ? e.Args[i + 1] : fallback; }
            try {
                await new InstallerService(u => Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(u)), Console.WriteLine)
                    .InstallAsync(Arg("--target", AppPaths.DefaultRoot), Arg("--reuse-models"), !e.Args.Contains("--no-shortcut"), CancellationToken.None);
                Shutdown(0);
            } catch (Exception error) { Console.Error.WriteLine(error); Shutdown(1); }
            return;
        }
        var rootIndex = Array.IndexOf(e.Args, "--app-root");
        if (rootIndex >= 0 && rootIndex + 1 < e.Args.Length) AppPaths.Root = Path.GetFullPath(e.Args[rootIndex + 1]);
        var setup = e.Args.Contains("--setup") || Path.GetFileName(Environment.ProcessPath!).Contains("Setup", StringComparison.OrdinalIgnoreCase) || !AppPaths.Ready;
        Window window = setup ? new SetupWindow(AppPaths.Ready ? AppPaths.Root : null) : new MainWindow();
        MainWindow = window; window.Show();
        // Windows passes a filename when a video is dropped onto the app
        // executable/shortcut or opened with the application.
        var video = e.Args.FirstOrDefault(value => File.Exists(value) &&
            new[] { ".mp4", ".mov", ".mkv", ".avi", ".webm", ".m4v" }.Contains(Path.GetExtension(value).ToLowerInvariant()));
        if (window is Views.MainWindow main && video != null) await main.OpenVideoAsync(video);
    }
    public static void DarkCaption(Window window)
    {
        window.SourceInitialized += (_, _) => { try { var value = 1; DwmSetWindowAttribute(new WindowInteropHelper(window).Handle, 20, ref value, sizeof(int)); } catch { } };
    }
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
