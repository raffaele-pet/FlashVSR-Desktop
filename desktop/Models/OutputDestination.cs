using System.IO;

namespace FlashVsr.Desktop.Models;

public static class OutputDestination
{
    public static string DefaultName(string input, Resolution resolution) =>
        $"{Path.GetFileNameWithoutExtension(input)}_FlashVSR_{resolution.Width}x{resolution.Height}";

    public static string Resolve(string input, string name, string folder, Resolution resolution, string defaultFolder)
    {
        name = string.IsNullOrWhiteSpace(name) ? DefaultName(input, resolution) : name.Trim();
        var stem = name.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;
        var device = stem.Split('.')[0].ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(stem) || stem is "." or ".." ||
            name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || stem.EndsWith('.') || stem.EndsWith(' ') ||
            device is "CON" or "PRN" or "AUX" or "NUL" ||
            (device.Length == 4 && (device.StartsWith("COM") || device.StartsWith("LPT")) && device[3] is >= '1' and <= '9'))
            throw new InvalidOperationException("Enter a valid file name without a folder path.");
        folder = string.IsNullOrWhiteSpace(folder) ? defaultFolder : folder.Trim();
        var output = Path.GetFullPath(Path.Combine(folder, stem + ".mp4"));
        if (output.Equals(Path.GetFullPath(input), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The original video cannot be overwritten.");
        return output;
    }
}
