using System.IO;
using System.Text;
using System.Text.Json;

namespace FlashVsr.Desktop.Services;

public sealed class WorkerService
{
    public async Task RunAsync(IEnumerable<string> arguments, Action<JsonElement> update, Action<string> log, CancellationToken token)
    {
        var args = new[] { "-u", Path.Combine(AppPaths.Engine, "bridge", "worker.py") }.Concat(arguments);
        var errors = new StringBuilder(); string? friendlyError = null;
        var code = await ProcessRunner.RunAsync(AppPaths.Python, args, AppPaths.Engine, line => {
            try {
                using var doc = JsonDocument.Parse(line);
                var item = doc.RootElement.Clone();
                if (item.TryGetProperty("event", out var e) && e.GetString() == "error") friendlyError = item.GetProperty("message").GetString();
                update(item);
            } catch (JsonException) { log(line); }
        }, line => { errors.AppendLine(line); log(line); }, token,
        new Dictionary<string, string> { ["TRITON_CACHE_DIR"] = Path.Combine(AppPaths.Engine, ".cache", "triton") });
        if (code != 0) throw new InvalidOperationException(friendlyError ?? $"Elaborazione non riuscita (codice {code}). Consulta i dettagli.");
    }
}
