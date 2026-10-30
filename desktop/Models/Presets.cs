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
    public static readonly IReadOnlyList<AspectPreset> All = JsonSerializer.Deserialize<List<AspectPreset>>(
        Assembly.GetExecutingAssembly().GetManifestResourceStream("presets.json")!,
        new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
}
