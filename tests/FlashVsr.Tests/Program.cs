using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using FlashVsr.Desktop.Models;
using FlashVsr.Desktop.Services;

static void Assert(bool test, string message) { if (!test) throw new Exception(message); }
var checks = 0;
foreach (var p in Presets.All) foreach (var size in new[] { p.Standard, p.High }) {
    var ratio = p.Id.Split(':').Select(int.Parse).ToArray();
    Assert(size.Width * ratio[1] == size.Height * ratio[0], "Aspect ratio mismatch");
    Assert(size.NativeWidth % 128 == 0 && size.NativeHeight % 128 == 0, "Invalid model canvas");
    Assert(size.Width % 2 == 0 && size.Height % 2 == 0, "Invalid H.264 size"); checks++;
}
var folder = Path.Combine(Path.GetTempPath(), "flashvsr-tests-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
try {
    // A real HTTP Range transfer exercises resume and checksum verification.
    using var socket = new TcpListener(IPAddress.Loopback, 0); socket.Start();
    var port = ((IPEndPoint)socket.LocalEndpoint).Port; socket.Stop();
    using var server = new HttpListener(); server.Prefixes.Add($"http://127.0.0.1:{port}/"); server.Start();
    var content = new byte[256000]; new Random(42).NextBytes(content);
    var expected = Convert.ToHexString(SHA256.HashData(content));
    var file = Path.Combine(folder, "download.bin"); await File.WriteAllBytesAsync(file + ".part", content[..8192]);
    var receivedRange = false;
    var respond = Task.Run(async () => {
        var context = await server.GetContextAsync();
        receivedRange = context.Request.Headers["Range"] == "bytes=8192-";
        context.Response.StatusCode = 206;
        context.Response.AddHeader("Content-Range", $"bytes 8192-{content.Length - 1}/{content.Length}");
        context.Response.ContentLength64 = content.Length - 8192;
        await context.Response.OutputStream.WriteAsync(content.AsMemory(8192)); context.Response.Close();
    });
    await Downloads.FileAsync($"http://127.0.0.1:{port}/model", file, expected, null, CancellationToken.None); await respond;
    Assert(receivedRange, "Download did not resume"); Assert((await File.ReadAllBytesAsync(file)).SequenceEqual(content), "Downloaded data differs");
    Assert(!File.Exists(file + ".part"), "Completed partial file not cleaned"); checks++;
    using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
    try { await Downloads.FileAsync($"http://127.0.0.1:{port}/never", Path.Combine(folder,"cancelled.bin"), null, null, cancelled.Token); throw new Exception("Cancelled transfer continued"); } catch (OperationCanceledException) { checks++; }
    using var processCts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
    var time = System.Diagnostics.Stopwatch.StartNew();
    try { await ProcessRunner.RunAsync("powershell.exe", ["-NoProfile","-NonInteractive","-Command","Start-Sleep -Seconds 20"],folder,null,null,processCts.Token); throw new Exception("Cancelled process continued"); } catch (OperationCanceledException) { Assert(time.Elapsed.TotalSeconds < 10,"Process cancellation was too slow"); checks++; }
    Console.WriteLine($"PASS: {checks} checks (16 geometries, resumed download, cancelled transfer, cancelled process tree)");
} finally { Directory.Delete(folder, true); }
