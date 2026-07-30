using System.IO;
using System.Windows;
using SentinelX.App.ViewModels;
using SentinelX.App.Views;
using SentinelX.Core.Interfaces;
using SentinelX.Core.Models;
using SentinelX.Infrastructure;
using SentinelX.Infrastructure.Monitoring;
using SentinelX.Infrastructure.Obs;
using SentinelX.Infrastructure.Networking;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace SentinelX.App;

public partial class App : Application
{
    private IHost? _host;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var logDirectory = Path.Combine(PortablePaths.ResolveWritableDirectory(), "logs");
        Directory.CreateDirectory(logDirectory);

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.File(
                Path.Combine(logDirectory, "sentinelx-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14)
            .CreateLogger();

        // A WinExe has no attached console, so the CLR's default unhandled-exception message
        // has nowhere visible to print — log it explicitly instead of losing it silently.
        DispatcherUnhandledException += (_, args) =>
        {
            Log.Fatal(args.Exception, "Unhandled dispatcher exception");
            Log.CloseAndFlush();
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            Log.Fatal(args.ExceptionObject as Exception, "Unhandled AppDomain exception");
            Log.CloseAndFlush();
        };

        // --obs-audit-dump and --soak run before the DI host is built, matching
        // BestStudioOBSChecker's headless CLI pattern: fast, minimal startup, no window ever
        // created for these one-shot diagnostic dumps.
        var obsAuditPath = TryGetArgValue(e.Args, "--obs-audit-dump");
        if (obsAuditPath is not null || HasFlag(e.Args, "--obs-audit-dump"))
        {
            Task.Run(() => RunObsAuditDumpAsync(obsAuditPath ?? "obs-audit.json")).GetAwaiter().GetResult();
            Log.CloseAndFlush();
            Shutdown(0);
            return;
        }

        if (HasFlag(e.Args, "--soak"))
        {
            var seconds = TryGetIntArgAt(e.Args, "--soak", 1) ?? 60;
            var kbps = TryGetIntArgAt(e.Args, "--soak", 2) ?? 6000;
            Task.Run(() => RunSoakAsync(seconds, kbps)).GetAwaiter().GetResult();
            Log.CloseAndFlush();
            Shutdown(0);
            return;
        }

        var scanJsonPath = TryGetArgValue(e.Args, "--scan-json");
        if (scanJsonPath is not null)
        {
            // Run on a thread-pool thread, not this (STA/UI) thread: the dispatcher's
            // message loop hasn't started yet at this point in OnStartup, so any awaited
            // continuation that captured the DispatcherSynchronizationContext would never
            // get to run if we blocked this thread with GetAwaiter().GetResult() directly.
            Task.Run(() => RunHeadlessScanAsync(scanJsonPath)).GetAwaiter().GetResult();
            Log.CloseAndFlush();
            Shutdown(0);
            return;
        }

        _host = Host.CreateDefaultBuilder()
            .UseSerilog()
            .ConfigureServices((_, services) =>
            {
                services.AddSentinelXInfrastructure();
                services.AddSingleton<MainWindowViewModel>();
                services.AddSingleton<DashboardViewModel>();
                services.AddSingleton<LiveMonitoringViewModel>();
                services.AddSingleton<EventViewerViewModel>();
                services.AddSingleton<SpeedTestViewModel>();
                services.AddSingleton<MainWindow>();
            })
            .Build();

        _host.Start();
        _host.Services.GetRequiredService<LiveMonitoringService>().Start();

        var mainWindow = _host.Services.GetRequiredService<MainWindow>();
        mainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _host?.Services.GetService<LiveMonitoringService>()?.Stop();
        _host?.Dispose();
        Log.CloseAndFlush();
        base.OnExit(e);
    }

    /// <summary>
    /// Swaps the active theme dictionary at runtime (index 0 of the app's merged
    /// dictionaries — see App.xaml).
    /// </summary>
    public void ApplyTheme(bool isDark)
    {
        var themeSource = new Uri($"Themes/{(isDark ? "Dark" : "Light")}.xaml", UriKind.Relative);
        Resources.MergedDictionaries[0] = new ResourceDictionary { Source = themeSource };
    }

    private static bool HasFlag(IReadOnlyList<string> args, string flag) =>
        args.Any(a => string.Equals(a, flag, StringComparison.OrdinalIgnoreCase));

    private static string? TryGetArgValue(IReadOnlyList<string> args, string flag)
    {
        for (var i = 0; i < args.Count - 1; i++)
        {
            if (string.Equals(args[i], flag, StringComparison.OrdinalIgnoreCase))
            {
                return args[i + 1];
            }
        }
        return null;
    }

    private static int? TryGetIntArgAt(IReadOnlyList<string> args, string flag, int offset)
    {
        var idx = -1;
        for (var i = 0; i < args.Count; i++)
        {
            if (string.Equals(args[i], flag, StringComparison.OrdinalIgnoreCase)) { idx = i; break; }
        }
        if (idx < 0 || idx + offset >= args.Count) return null;
        return int.TryParse(args[idx + offset], out var v) ? v : null;
    }

    /// <summary>
    /// Fast OBS-only config/log dump — no DI host, no window. Mirrors
    /// BestStudioOBSChecker's --audit-dump mode.
    /// </summary>
    private static async Task RunObsAuditDumpAsync(string outputPath)
    {
        var audit = await Task.Run(() => new ObsConfigService(new ObsLogAnalyzer()).ReadAudit());
        var json = System.Text.Json.JsonSerializer.Serialize(
            audit, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(outputPath, json);
    }

    /// <summary>
    /// Headless network soak test — no DI host, no window. Mirrors BestStudioOBSChecker's
    /// --soak mode, writing a flat key=value summary next to the exe.
    /// </summary>
    private static async Task RunSoakAsync(int seconds, int kbps)
    {
        var result = await new StabilityTestService().RunAsync(
            seconds, kbps, onPing: _ => { }, onStatus: _ => { }, CancellationToken.None);

        var lines = new[]
        {
            $"DurationSec={result.DurationSec}",
            $"TargetKbps={result.TargetKbps}",
            $"PingCount={result.PingCount}",
            $"PingTimeouts={result.PingTimeouts}",
            $"PingSpikes={result.PingSpikes}",
            $"AvgPingMs={result.AvgPingMs:0.0}",
            $"MaxPingMs={result.MaxPingMs:0.0}",
            $"TargetAchievedPct={result.TargetAchievedPct:0.0}",
            $"StallSeconds={result.StallSeconds:0.0}",
            $"Verdict={result.Verdict}",
            $"Error={result.Error ?? ""}"
        };
        await File.WriteAllLinesAsync("soak-result.txt", lines);
    }

    /// <summary>
    /// Runs every registered engine without showing the WPF window, dumping results to JSON.
    /// Used for automated verification of real engine output during development.
    /// </summary>
    private static async Task RunHeadlessScanAsync(string outputPath)
    {
        await using var provider = new ServiceCollection()
            .AddSentinelXInfrastructure()
            .BuildServiceProvider();

        var engines = provider.GetServices<IDiagnosticEngine>().ToList();
        var scoringService = provider.GetRequiredService<IScoringService>();
        var historyRepository = provider.GetRequiredService<IHistoryRepository>();

        var results = await Task.WhenAll(engines.Select(engine => engine.RunAsync(CancellationToken.None)));

        var categoryScores = results.ToDictionary(r => r.Category, r => r.HealthScore);
        var overallScore = scoringService.ComputeOverallScore(categoryScores);
        var findings = results.SelectMany(r => r.Findings).ToList();

        var snapshot = new ScanSnapshot(DateTimeOffset.Now, overallScore, categoryScores, findings);
        await historyRepository.SaveScanAsync(snapshot, CancellationToken.None);

        var report = new
        {
            overallScore,
            categoryScores = categoryScores.ToDictionary(kv => kv.Key.ToString(), kv => kv.Value),
            engines = results.Select(r => new
            {
                category = r.Category.ToString(),
                healthScore = r.HealthScore,
                durationMs = r.Duration.TotalMilliseconds,
                error = r.Error,
                findings = r.Findings
            })
        };

        var json = System.Text.Json.JsonSerializer.Serialize(
            report, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });

        await File.WriteAllTextAsync(outputPath, json);
    }
}
