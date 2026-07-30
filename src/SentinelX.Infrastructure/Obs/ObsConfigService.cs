using System.Text.Json;
using Microsoft.Win32;

namespace SentinelX.Infrastructure.Obs;

/// <summary>
/// Reads OBS Studio configuration (read-only). The stream key is never stored —
/// only a boolean saying whether one is set.
/// </summary>
public sealed class ObsConfigService
{
    private readonly ObsLogAnalyzer _logAnalyzer;

    public ObsConfigService(ObsLogAnalyzer logAnalyzer)
    {
        _logAnalyzer = logAnalyzer;
    }

    public ObsAudit ReadAudit()
    {
        var audit = new ObsAudit { InstallPath = FindInstallPath() };
        var cfgRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "obs-studio");

        if (!Directory.Exists(cfgRoot))
        {
            audit.State = audit.InstallPath is null ? ObsState.NotInstalled : ObsState.InstalledNotConfigured;
            return audit;
        }

        audit.State = ObsState.Configured;
        audit.ConfigRoot = cfgRoot;

        try
        {
            ReadProfile(audit, cfgRoot);
        }
        catch (Exception ex)
        {
            audit.Notes.Add("Error reading OBS config: " + ex.Message);
        }

        try
        {
            audit.Log = _logAnalyzer.AnalyzeLatestLog(Path.Combine(cfgRoot, "logs"));
        }
        catch (Exception ex)
        {
            audit.Notes.Add("Error reading OBS logs: " + ex.Message);
        }

        return audit;
    }

    private static void ReadProfile(ObsAudit audit, string cfgRoot)
    {
        // OBS 31+ stores the active profile in user.ini; older versions in global.ini
        var userIni = IniFile.TryLoad(Path.Combine(cfgRoot, "user.ini"));
        var globalIni = IniFile.TryLoad(Path.Combine(cfgRoot, "global.ini"));
        var profileDir = userIni?.Get("Basic", "ProfileDir")
                         ?? globalIni?.Get("Basic", "ProfileDir")
                         ?? userIni?.Get("Basic", "Profile")
                         ?? globalIni?.Get("Basic", "Profile");

        var profilesRoot = Path.Combine(cfgRoot, "basic", "profiles");
        if (profileDir is null && Directory.Exists(profilesRoot))
        {
            var dirs = Directory.GetDirectories(profilesRoot);
            if (dirs.Length == 1) profileDir = Path.GetFileName(dirs[0]);
        }
        if (profileDir is null)
        {
            audit.Notes.Add("Could not determine the active OBS profile.");
            return;
        }
        audit.ProfileName = profileDir;

        var profPath = Path.Combine(profilesRoot, profileDir);
        if (!Directory.Exists(profPath))
        {
            // Profile display name can differ from the directory name — best-effort match
            var match = Directory.Exists(profilesRoot)
                ? Directory.GetDirectories(profilesRoot)
                    .FirstOrDefault(d => Path.GetFileName(d).Equals(profileDir, StringComparison.OrdinalIgnoreCase))
                : null;
            if (match is null)
            {
                audit.Notes.Add($"Profile folder '{profileDir}' not found under basic\\profiles.");
                return;
            }
            profPath = match;
        }

        var basic = IniFile.TryLoad(Path.Combine(profPath, "basic.ini"));
        if (basic is not null)
        {
            audit.OutputMode = basic.Get("Output", "Mode") ?? "Simple";
            var advanced = audit.OutputMode.Equals("Advanced", StringComparison.OrdinalIgnoreCase);

            if (advanced)
            {
                audit.EncoderId = basic.Get("AdvOut", "Encoder") ?? "obs_x264";
                var v = basic.GetInt("AdvOut", "VBitrate"); // present when using default adv settings
                if (v is not null) { audit.VBitrateKbps = v.Value; audit.VBitrateIsDefault = false; }
                var track = basic.GetInt("AdvOut", "TrackIndex") ?? 1;
                var a = basic.GetInt("AdvOut", $"Track{track}Bitrate");
                if (a is not null) { audit.ABitrateKbps = a.Value; audit.ABitrateIsDefault = false; }
            }
            else
            {
                audit.EncoderId = basic.Get("SimpleOutput", "StreamEncoder") ?? "x264";
                var v = basic.GetInt("SimpleOutput", "VBitrate");
                if (v is not null) { audit.VBitrateKbps = v.Value; audit.VBitrateIsDefault = false; }
                var a = basic.GetInt("SimpleOutput", "ABitrate");
                if (a is not null) { audit.ABitrateKbps = a.Value; audit.ABitrateIsDefault = false; }
            }

            audit.BaseCX = basic.GetInt("Video", "BaseCX") ?? 0;
            audit.BaseCY = basic.GetInt("Video", "BaseCY") ?? 0;
            audit.OutCX = basic.GetInt("Video", "OutputCX") ?? audit.BaseCX;
            audit.OutCY = basic.GetInt("Video", "OutputCY") ?? audit.BaseCY;
            audit.Fps = ReadFps(basic);
        }
        else
        {
            audit.Notes.Add("basic.ini not found — OBS is using all-default output settings (2500 kbps, x264).");
        }

        // Advanced-mode keyframe interval lives in streamEncoder.json
        var encJsonPath = Path.Combine(profPath, "streamEncoder.json");
        if (File.Exists(encJsonPath))
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(encJsonPath));
                if (doc.RootElement.TryGetProperty("keyint_sec", out var k) && k.TryGetInt32(out var keyint))
                    audit.KeyintSec = keyint;
            }
            catch { audit.Notes.Add("Could not parse streamEncoder.json."); }
        }

        // service.json: server URL + whether a stream key is set (key itself is discarded)
        var servicePath = Path.Combine(profPath, "service.json");
        if (File.Exists(servicePath))
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(servicePath));
                if (doc.RootElement.TryGetProperty("settings", out var settings))
                {
                    if (settings.TryGetProperty("service", out var svc))
                        audit.ServiceName = svc.GetString();
                    if (settings.TryGetProperty("server", out var server))
                        audit.ServerUrl = StripQuery(server.GetString());
                    if (settings.TryGetProperty("key", out var key))
                        audit.StreamKeyPresent = !string.IsNullOrEmpty(key.GetString());
                }
            }
            catch { audit.Notes.Add("Could not parse service.json."); }
        }
        else
        {
            audit.Notes.Add("No streaming service configured yet (service.json missing).");
        }
    }

    private static double ReadFps(IniFile basic)
    {
        var type = basic.GetInt("Video", "FPSType") ?? 0;
        switch (type)
        {
            case 1:
                return basic.GetInt("Video", "FPSInt") ?? 30;
            case 2:
                var num = basic.GetDouble("Video", "FPSNum") ?? 30;
                var den = basic.GetDouble("Video", "FPSDen") ?? 1;
                return den > 0 ? num / den : 30;
            default:
                var common = basic.Get("Video", "FPSCommon") ?? "30";
                var firstToken = common.Split(' ')[0];
                return double.TryParse(firstToken, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var f) ? f : 30;
        }
    }

    private static string? StripQuery(string? url)
    {
        if (string.IsNullOrEmpty(url)) return url;
        var q = url.IndexOf('?');
        return q >= 0 ? url[..q] : url;
    }

    public static string? FindInstallPath()
    {
        foreach (var p in new[]
                 {
                     @"C:\Program Files\obs-studio",
                     @"C:\Program Files (x86)\obs-studio"
                 })
        {
            if (Directory.Exists(p)) return p;
        }
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\OBS Studio");
            var val = key?.GetValue("")?.ToString();
            if (!string.IsNullOrEmpty(val) && Directory.Exists(val)) return val;
        }
        catch { }
        return null;
    }
}
