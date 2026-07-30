namespace SentinelX.Infrastructure;

/// <summary>
/// Portable-first storage location: prefer writing next to the executable (so history/logs
/// travel with a USB/technician-mode install) and fall back to the per-user local app data
/// folder when the exe directory isn't writable (read-only media, Program Files).
/// </summary>
public static class PortablePaths
{
    public static string ResolveWritableDirectory()
    {
        var exeDirectory = AppContext.BaseDirectory;
        if (IsWritable(exeDirectory))
        {
            return exeDirectory;
        }

        var fallbackDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SentinelX");
        Directory.CreateDirectory(fallbackDirectory);
        return fallbackDirectory;
    }

    private static bool IsWritable(string directory)
    {
        try
        {
            var probePath = Path.Combine(directory, $".write-test-{Guid.NewGuid():N}");
            File.WriteAllText(probePath, string.Empty);
            File.Delete(probePath);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
