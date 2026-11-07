using System.Text.Json;
using System.Reflection;

namespace FlashVsr.Desktop.Models;

public record Resolution(int Width, int Height, int NativeWidth, int NativeHeight)
{
    public string Label => $"{Width:N0} × {Height:N0}".Replace(",", "").Replace(".", "");
    public string NativeLabel => $"{NativeWidth} × {NativeHeight}";
}
public record AspectPreset(string Id, string Name, Resolution Standard, Resolution High)
{
    public string Display => $"{Id}  ·  {Name}";
    public override string ToString() => Display;
}
public static class Presets
{
    public static Resolution ForVideo(int width, int height, bool high)
    {
        if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        foreach (var preset in All) {
            var ratio = preset.Id.Split(':').Select(int.Parse).ToArray();
            if ((long)width * ratio[1] == (long)height * ratio[0]) return high ? preset.High : preset.Standard;
        }
        // Keep the model's pixel budget independent of the source dimensions.
        // Only the internal canvas is rounded; the output uses the source ratio.
        var budget = high ? 2560d * 1408 : 2048d * 1152;
        var scale = Math.Sqrt(budget / width / height);
        var nativeWidth = Math.Max(128, (int)Math.Round(width * scale / 128) * 128);
        var nativeHeight = Math.Max(128, (int)Math.Round(height * scale / 128) * 128);
        var a = width; var b = height;
        while (b != 0) (a, b) = (b, a % b);
        var unitWidth = width / a; var unitHeight = height / a;
        var evenStep = unitWidth % 2 != 0 || unitHeight % 2 != 0 ? 2 : 1;
        var multiple = Math.Max(evenStep, (int)Math.Round(a * scale / evenStep) * evenStep);
        return new(unitWidth * multiple, unitHeight * multiple, nativeWidth, nativeHeight);
    }
    public static readonly IReadOnlyList<AspectPreset> All = JsonSerializer.Deserialize<List<AspectPreset>>(
        Assembly.GetExecutingAssembly().GetManifestResourceStream("presets.json")!,
        new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
}
