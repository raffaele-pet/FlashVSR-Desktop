using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.RegularExpressions;

namespace FlashVsr.Desktop.Services;
public static class DriverService
{
    public static async Task InstallOfficialManagerAsync(Action<string, double?> progress, CancellationToken token)
    {
        // Resolve the current x64 installer from NVIDIA's own page, rather
        // than trusting a third-party driver service or stale download link.
        using var http = new HttpClient();
        var page = await http.GetStringAsync("https://www.nvidia.com/en-us/software/nvidia-app/", token);
        var matches = Regex.Matches(page, "https://us\\.download\\.nvidia\\.com/nvapp/client/[^\\\"'<>\\s]+\\.exe");
        var url = matches.Select(m => m.Value).FirstOrDefault(s => !s.Contains("arm64", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("Collegamento NVIDIA non trovato. Scarica il driver dal sito ufficiale NVIDIA.");
        var folder = Path.Combine(AppPaths.Data, "downloads"); Directory.CreateDirectory(folder);
        var file = Path.Combine(folder, "NVIDIA-App-Setup.exe");
        if (File.Exists(file)) File.Delete(file);
        await Downloads.FileAsync(url, file, null, (n, total) => progress("Download dell’installazione ufficiale NVIDIA…", total > 0 ? (double)n / total : null), token);
        var quoted = "'" + file.Replace("'", "''") + "'";
        await ProcessRunner.CaptureAsync("powershell.exe", ["-NoProfile", "-NonInteractive", "-Command", $"$s=Get-AuthenticodeSignature -LiteralPath {quoted}; if($s.Status -ne 'Valid' -or $s.SignerCertificate.Subject -notlike '*NVIDIA*'){{exit 1}}"], folder, token);
        progress("Completa la procedura NVIDIA e l’installazione del driver. Potrebbe servire un riavvio.", null);
        using var installer = Process.Start(new ProcessStartInfo(file) { UseShellExecute = true, Verb = "runas" })!;
        await installer.WaitForExitAsync(token);
    }
}
