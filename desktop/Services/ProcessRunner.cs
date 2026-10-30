using System.Diagnostics;
using System.IO;
using System.Text;

namespace FlashVsr.Desktop.Services;

public static class ProcessRunner
{
    public static async Task<int> RunAsync(string file, IEnumerable<string> args, string cwd,
        Action<string>? output, Action<string>? error, CancellationToken cancellationToken,
        IReadOnlyDictionary<string, string>? environment = null)
    {
        var start = new ProcessStartInfo(file) {
            WorkingDirectory = cwd, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
        };
        foreach (var arg in args) start.ArgumentList.Add(arg);
        start.Environment["PYTHONUTF8"] = "1";
        start.Environment["PYTHONUNBUFFERED"] = "1";
        start.Environment["PIP_DISABLE_PIP_VERSION_CHECK"] = "1";
        if (environment != null) foreach (var item in environment) start.Environment[item.Key] = item.Value;
        using var process = new Process { StartInfo = start, EnableRaisingEvents = true };
        process.Start();
        using var registration = cancellationToken.Register(() => {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
        });
        var readOut = ReadAsync(process.StandardOutput, output);
        var readErr = ReadAsync(process.StandardError, error);
        await process.WaitForExitAsync(CancellationToken.None);
        await Task.WhenAll(readOut, readErr);
        cancellationToken.ThrowIfCancellationRequested();
        return process.ExitCode;
    }
    private static async Task ReadAsync(StreamReader reader, Action<string>? callback)
    {
        while (await reader.ReadLineAsync() is { } line) callback?.Invoke(line);
    }
    public static async Task<string> CaptureAsync(string file, IEnumerable<string> args, string cwd, CancellationToken token)
    {
        var stdout = new StringBuilder(); var stderr = new StringBuilder();
        var exit = await RunAsync(file, args, cwd, s => stdout.AppendLine(s), s => stderr.AppendLine(s), token);
        if (exit != 0) throw new InvalidOperationException(stderr.ToString().Trim() is { Length: > 0 } detail ? detail : $"{Path.GetFileName(file)}: errore {exit}");
        return stdout.ToString().Trim();
    }
}
