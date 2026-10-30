using System.IO;
using System.Text.Json;

namespace FlashVsr.Desktop.Services;

public record InstallInfo(string Version, string EnginePath, string PythonPath, DateTime InstalledUtc);
public static class AppPaths
{
    public const string Version = "1.0.0";
    public const string Marker = "flashvsr-install.json";
    public static string DefaultRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "FlashVSR Desktop");
    public static string Data => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FlashVSR Desktop");
    public static string DefaultOutput => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "FlashVSR");
    public static string Root { get; set; } = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
    public static string Engine => Path.Combine(Root, "engine");
    public static string Python => Path.Combine(Root, ".venv", "Scripts", "python.exe");
    public static bool Ready => File.Exists(Path.Combine(Root, Marker)) && File.Exists(Python) && File.Exists(Path.Combine(Engine, "bridge", "worker.py"));
    public static void SaveMarker(string root) => File.WriteAllText(Path.Combine(root, Marker), JsonSerializer.Serialize(
        new InstallInfo(Version, "engine", ".venv/Scripts/python.exe", DateTime.UtcNow), new JsonSerializerOptions { WriteIndented = true }));
    public static void OpenPath(string path) => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
}
