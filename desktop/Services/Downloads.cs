using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;

namespace FlashVsr.Desktop.Services;

public static class Downloads
{
    private static readonly HttpClient Http = new(new HttpClientHandler { AllowAutoRedirect = true }) { Timeout = Timeout.InfiniteTimeSpan };
    public static async Task FileAsync(string url, string destination, string? sha256,
        Action<long, long?>? progress, CancellationToken token)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        if (File.Exists(destination) && (sha256 == null || await HashMatches(destination, sha256, token))) return;
        var partial = destination + ".part";
        for (var attempt = 0; attempt < 5; attempt++)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                var offset = File.Exists(partial) ? new FileInfo(partial).Length : 0;
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.UserAgent.ParseAdd("FlashVSR-Desktop/1.0");
                if (offset > 0) request.Headers.Range = new RangeHeaderValue(offset, null);
                using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
                if (response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable) { File.Delete(partial); continue; }
                response.EnsureSuccessStatusCode();
                if (response.StatusCode != HttpStatusCode.PartialContent) offset = 0;
                var total = response.Content.Headers.ContentRange?.Length ?? (response.Content.Headers.ContentLength is { } length ? length + offset : null);
                await using (var input = await response.Content.ReadAsStreamAsync(token))
                await using (var output = new FileStream(partial, offset > 0 ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.Read, 131072, true))
                {
                    var buffer = new byte[131072]; var done = offset; var last = DateTime.MinValue;
                    int read;
                    while ((read = await input.ReadAsync(buffer, token)) > 0)
                    {
                        await output.WriteAsync(buffer.AsMemory(0, read), token); done += read;
                        if ((DateTime.UtcNow - last).TotalMilliseconds > 150) { progress?.Invoke(done, total); last = DateTime.UtcNow; }
                    }
                    if (total is { } expected && done != expected) throw new IOException("Download incompleto: ripresa automatica.");
                    progress?.Invoke(done, total);
                }
                if (sha256 != null && !await HashMatches(partial, sha256, token)) { File.Delete(partial); throw new IOException("Controllo SHA-256 fallito. Nuovo download."); }
                File.Move(partial, destination, true); return;
            }
            catch (Exception error) when (error is HttpRequestException or IOException && !token.IsCancellationRequested && attempt < 4)
            { await Task.Delay(TimeSpan.FromSeconds(2 * (attempt + 1)), token); }
        }
        throw new IOException("Impossibile completare il download.");
    }
    public static async Task<bool> HashMatches(string file, string expected, CancellationToken token)
    {
        await using var stream = File.OpenRead(file);
        var hash = await SHA256.HashDataAsync(stream, token);
        return Convert.ToHexString(hash).Equals(expected, StringComparison.OrdinalIgnoreCase);
    }
}
