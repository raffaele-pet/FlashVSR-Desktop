using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Win32;

namespace FlashVsr.Desktop.Services;

public record SetupUpdate(int Step, string Message, double? Progress = null, bool Done = false);
public sealed class InstallerService
{
    private const string UvVersion = "0.11.32";
    private const string ModelRevision = "46b79520946ac67dec94d1566490690953c7debc";
    public static readonly string[] Steps = ["Controllo del PC", "Componenti Microsoft", "Applicazione e motore", "Python 3.10", "PyTorch e CUDA", "Dipendenze e Triton", "Modello Turbo", "Verifica sulla GPU", "Collegamenti e completamento"];
    private readonly Action<SetupUpdate> _report;
    private readonly Action<string> _log;
    public InstallerService(Action<SetupUpdate> report, Action<string> log) { _report = report; _log = log; }
    private void Update(int step, string message, double? fraction = null, bool done = false) => _report(new(step, message, fraction, done));
    private static string Py(string root) => Path.Combine(root, ".venv", "Scripts", "python.exe");
    private static string QuotePs(string value) => "'" + value.Replace("'", "''") + "'";

    public async Task InstallAsync(string root, string? reuseModels, bool desktopShortcut, CancellationToken token)
    {
        root = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
        if (root == Path.GetPathRoot(root) || root.Equals(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Scegli una cartella dedicata a FlashVSR Desktop.");
        if (Directory.Exists(root) && Directory.EnumerateFileSystemEntries(root).Any() && !File.Exists(Path.Combine(root, AppPaths.Marker)) && !File.Exists(Path.Combine(root, ".flashvsr-setup")))
            throw new InvalidOperationException("La cartella scelta contiene altri file. Scegli una cartella vuota dedicata all’applicazione.");
        Directory.CreateDirectory(root);
        await File.WriteAllTextAsync(Path.Combine(root, ".flashvsr-setup"), AppPaths.Version, token);
        using var log = new StreamWriter(Path.Combine(root, "installation.log"), append: true) { AutoFlush = true };
        void Log(string text) { lock (log) log.WriteLine($"{DateTime.Now:HH:mm:ss} {text}"); _log(text); }
        async Task Exec(string file, IEnumerable<string> args, string cwd, IReadOnlyDictionary<string, string>? env = null)
        {
            var code = await ProcessRunner.RunAsync(file, args, cwd, Log, Log, token, env);
            if (code != 0) throw new InvalidOperationException($"{Path.GetFileName(file)} ha restituito errore {code}. Consulta i dettagli dell’installazione.");
        }
        Update(0, "Verifica di Windows, GPU e spazio disponibile…");
        if (!Environment.Is64BitOperatingSystem || !OperatingSystem.IsWindowsVersionAtLeast(10))
            throw new InvalidOperationException("Serve Windows 10/11 a 64 bit.");
        if (new DriveInfo(Path.GetPathRoot(root)!).AvailableFreeSpace < 22L * 1024 * 1024 * 1024)
            throw new InvalidOperationException("Servono almeno 22 GB liberi per ambiente, modelli e download temporanei.");
        string gpu;
        try { gpu = await ProcessRunner.CaptureAsync("nvidia-smi.exe", ["--query-gpu=name,memory.total", "--format=csv,noheader"], root, token); }
        catch (Exception ex) when (ex is not OperationCanceledException) {
            var hardware = await ProcessRunner.CaptureAsync("powershell.exe", ["-NoProfile", "-NonInteractive", "-Command", "Get-CimInstance Win32_VideoController | Where-Object {$_.PNPDeviceID -like '*VEN_10DE*'} | Select-Object -ExpandProperty Name"], root, token);
            if (string.IsNullOrWhiteSpace(hardware)) throw new InvalidOperationException("Non è stata rilevata una GPU NVIDIA. Il motore richiede una GPU NVIDIA Ampere o successiva; non supporta CPU, AMD o Intel.", ex);
            await DriverService.InstallOfficialManagerAsync((message, fraction) => Update(0, message, fraction), token);
            try { gpu = await ProcessRunner.CaptureAsync("nvidia-smi.exe", ["--query-gpu=name,memory.total", "--format=csv,noheader"], root, token); }
            catch (Exception driverError) when (driverError is not OperationCanceledException) { throw new InvalidOperationException("Completa l’installazione del driver NVIDIA, riavvia Windows se richiesto e premi Riprova.", driverError); }
        }
        Log(gpu); Update(0, gpu, 1, true);

        Update(1, "Verifica Microsoft Visual C++ Runtime…");
        using (var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\x64"))
        {
            if (Convert.ToInt32(key?.GetValue("Installed", 0) ?? 0) != 1)
            {
                var redist = Path.Combine(root, "vc_redist.x64.exe");
                await Downloads.FileAsync("https://aka.ms/vs/17/release/vc_redist.x64.exe", redist, null, (n, total) => Update(1, "Download dei componenti Microsoft…", total > 0 ? (double)n / total : null), token);
                var signature = await ProcessRunner.CaptureAsync("powershell.exe", ["-NoProfile", "-NonInteractive", "-Command", $"$s=Get-AuthenticodeSignature -LiteralPath {QuotePs(redist)}; if($s.Status -ne 'Valid' -or $s.SignerCertificate.Subject -notlike '*Microsoft*'){{exit 1}}"], root, token);
                using var install = Process.Start(new ProcessStartInfo(redist, "/install /quiet /norestart") { UseShellExecute = true, Verb = "runas" })!;
                await install.WaitForExitAsync(token);
                if (install.ExitCode is not (0 or 1638 or 3010)) throw new InvalidOperationException("Installazione dei componenti Microsoft non riuscita.");
                if (install.ExitCode == 3010) Log("Windows segnala che è consigliato un riavvio dopo l’installazione.");
                File.Delete(redist);
            }
        }
        Update(1, "Componenti Microsoft disponibili", 1, true);

        Update(2, "Installazione dell’applicazione e del motore FlashVSR+…");
        var app = Path.Combine(root, "FlashVSRDesktop.exe");
        if (!Path.GetFullPath(Environment.ProcessPath!).Equals(app, StringComparison.OrdinalIgnoreCase)) File.Copy(Environment.ProcessPath!, app, true);
        var engine = Path.Combine(root, "engine"); Directory.CreateDirectory(engine);
        await using (var payload = Assembly.GetExecutingAssembly().GetManifestResourceStream("engine.zip") ?? throw new InvalidOperationException("Pacchetto del motore non incluso. Usa il file Setup della release."))
        using (var zip = new ZipArchive(payload, ZipArchiveMode.Read))
        {
            foreach (var entry in zip.Entries)
            {
                token.ThrowIfCancellationRequested();
                var target = Path.GetFullPath(Path.Combine(engine, entry.FullName));
                if (!target.StartsWith(engine + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Percorso non valido nel pacchetto.");
                if (entry.Name.Length == 0) { Directory.CreateDirectory(target); continue; }
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                entry.ExtractToFile(target, true);
            }
        }
        Update(2, "Applicazione e codice del motore installati", 1, true);
        var tools = Path.Combine(root, "tools"); Directory.CreateDirectory(tools);
        var uv = Path.Combine(tools, "uv.exe");
        var environment = new Dictionary<string, string> {
            ["UV_PYTHON_INSTALL_DIR"] = Path.Combine(root, "python"),
            ["UV_PYTHON_BIN_DIR"] = Path.Combine(tools, "python-bin"),
            ["UV_HTTP_TIMEOUT"] = "600",
            ["TRITON_CACHE_DIR"] = Path.Combine(engine, ".cache", "triton")
        };
        if (!File.Exists(uv))
        {
            var asset = $"uv-x86_64-pc-windows-msvc.zip";
            var prefix = $"https://github.com/astral-sh/uv/releases/download/{UvVersion}/";
            var checksum = Path.Combine(tools, "uv.sha256");
            await Downloads.FileAsync(prefix + asset + ".sha256", checksum, null, null, token);
            var expected = (await File.ReadAllTextAsync(checksum, token)).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)[0];
            var archive = Path.Combine(tools, "uv.zip");
            Update(3, "Download del gestore Python…");
            await Downloads.FileAsync(prefix + asset, archive, expected, (n, total) => Update(3, "Download del gestore Python…", total > 0 ? (double)n / total : null), token);
            using var zip = ZipFile.OpenRead(archive);
            var item = zip.Entries.Single(e => e.Name == "uv.exe"); item.ExtractToFile(uv, true);
        }
        Update(3, "Download e creazione di Python 3.10 isolato…");
        await Exec(uv, ["python", "install", "3.10", "--no-bin", "--no-registry"], root, environment);
        if (!File.Exists(Py(root))) await Exec(uv, ["venv", "--seed", "--python", "3.10", Path.Combine(root, ".venv")], root, environment);
        Update(3, "Python 3.10 pronto, separato dal Python di sistema", 1, true);

        Update(4, "Installazione PyTorch 2.7.1 e CUDA 12.8 (download di grandi dimensioni)…");
        await Exec(uv, ["pip", "install", "--python", Py(root), "torch==2.7.1", "--index-url", "https://download.pytorch.org/whl/cu128"], root, environment);
        Update(4, "PyTorch con CUDA installato", 1, true);
        Update(5, "Installazione Triton, FFmpeg e dipendenze del motore…");
        await Exec(uv, ["pip", "install", "--python", Py(root), "-r", Path.Combine(engine, "requirements-desktop.txt")], root, environment);
        await Exec(uv, ["pip", "install", "--python", Py(root), "-e", engine, "--no-deps"], root, environment);
        await Exec(uv, ["pip", "install", "--python", Py(root), "-e", Path.Combine(engine, "operators", "flashvsr_attn"), "--no-deps"], root, environment);
        await Exec(uv, ["pip", "check", "--python", Py(root)], root, environment);
        Update(5, "Triton, FFmpeg e dipendenze installati", 1, true);

        var models = new[] {
            (Path: "turbo/dit.safetensors", Size: 5676070392L, Sha: "7862b92178176fa3aebc34c6c071daca6b56ab1dddb8e7ff9a01c47f5226ec8b"),
            (Path: "turbo/lq_proj_in.safetensors", Size: 1151384272L, Sha: "e77b378ae12342eefbb58f924256f362a151b93ce73dd32808fcdaca9c051496"),
            (Path: "TCDecoder.ckpt", Size: 189018333L, Sha: "e224bdcf2f52745cbf4d393ff5374c2ba09e90285d5d19062d2bf63b915b6161")
        };
        long completed = 0, modelTotal = models.Sum(m => m.Size);
        foreach (var model in models)
        {
            var target = Path.Combine(engine, "ckpts", model.Path.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            var source = string.IsNullOrWhiteSpace(reuseModels) ? null : Path.Combine(reuseModels, "ckpts", model.Path.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(target) && source != null && File.Exists(source))
            {
                Update(6, $"Verifica del modello locale {model.Path}…", (double)completed / modelTotal);
                if (new FileInfo(source).Length == model.Size && await Downloads.HashMatches(source, model.Sha, token))
                {
                    // Hard links retain their data even if the original path is deleted.
                    if (!CreateHardLink(target, source, IntPtr.Zero)) File.Copy(source, target);
                    Log($"Modello locale riutilizzato: {model.Path}");
                }
            }
            var before = completed;
            await Downloads.FileAsync($"https://huggingface.co/ruchangyao/FlashVSR_plus/resolve/{ModelRevision}/{model.Path}", target, model.Sha,
                (n, total) => Update(6, $"{model.Path} · {n / 1073741824d:F2} / {model.Size / 1073741824d:F2} GB", (double)(before + n) / modelTotal), token);
            completed += model.Size;
        }
        Update(6, "Modello Turbo e decoder verificati con SHA-256", 1, true);
        Update(7, "Verifica CUDA e compilazione dell’attenzione Triton sulla GPU…");
        await Exec(Py(root), ["-u", Path.Combine(engine, "bridge", "worker.py"), "--doctor"], engine, environment);
        Update(7, "Calcolo BF16 e attenzione Triton verificati", 1, true);
        Update(8, "Creazione dei collegamenti…");
        CreateShortcut(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs", "FlashVSR Desktop.lnk"), app, root);
        if (desktopShortcut) CreateShortcut(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "FlashVSR Desktop.lnk"), app, root);
        // A double-clickable repair entry keeps setup available without a terminal.
        CreateShortcut(Path.Combine(root, "Ripara installazione.lnk"), app, root, "--setup");
        AppPaths.SaveMarker(root); File.Delete(Path.Combine(root, ".flashvsr-setup"));
        Update(8, "Installazione completata", 1, true);
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CreateHardLink(string fileName, string existingFileName, IntPtr securityAttributes);
    public static void CreateShortcut(string path, string app, string cwd, string arguments = "")
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var type = Type.GetTypeFromProgID("WScript.Shell")!;
        dynamic shell = Activator.CreateInstance(type)!;
        dynamic shortcut = shell.CreateShortcut(path);
        shortcut.TargetPath = app; shortcut.WorkingDirectory = cwd;
        shortcut.Arguments = arguments; shortcut.IconLocation = app + ",0";
        shortcut.Description = "FlashVSR+ · Video super-resolution"; shortcut.Save();
        Marshal.FinalReleaseComObject(shortcut); Marshal.FinalReleaseComObject(shell);
    }
}
