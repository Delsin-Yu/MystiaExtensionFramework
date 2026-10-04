using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;

namespace Mystia.Syringe;

public static class Program
{
    public static int Main(string[] args)
    {
        var launcherDirectory = Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory);
        var configPath = ValueOf(args, "--config") ?? Path.Combine(launcherDirectory, "launcher.json");
        if (!File.Exists(configPath))
        {
            Console.Error.WriteLine($"launcher.json was not found: {configPath}");
            return 1;
        }

        LauncherFile? config;
        try
        {
            config = JsonSerializer.Deserialize<LauncherFile>(File.ReadAllText(configPath), new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
            });
        }
        catch (JsonException error)
        {
            Console.Error.WriteLine($"launcher.json is not valid JSON: {error.Message}");
            return 1;
        }

        if (config is null || string.IsNullOrWhiteSpace(config.GameExe))
        {
            Console.Error.WriteLine("launcher.json is missing gameExe.");
            return 1;
        }

        var gameExe = Path.GetFullPath(config.GameExe);
        var gameDirectory = Path.GetDirectoryName(gameExe);
        if (string.IsNullOrEmpty(gameDirectory))
        {
            Console.Error.WriteLine($"launcher.json names a gameExe without a directory: {config.GameExe}");
            return 1;
        }

        var payload = ProxyInstall.FindPayload(launcherDirectory);

        if (args.Contains("--uninstall"))
        {
            Console.WriteLine(ProxyInstall.Uninstall(gameDirectory, launcherDirectory, payload).Message);
            return 0;
        }

        var guard = GameAssemblyGuard.Check(gameExe, config.ExpectedGameAssemblySha256);
        if (!guard.Ok)
        {
            Console.Error.WriteLine(guard.Message);
            return 1;
        }

        if (config.SteamAppId <= 0)
        {
            Console.Error.WriteLine("launcher.json is missing steamAppId.");
            return 1;
        }

        var bootstrap = Path.Combine(launcherDirectory, "Mystia.Bootstrap.dll");
        if (!File.Exists(bootstrap))
        {
            Console.Error.WriteLine($"The proxy would load a bootstrap DLL that is not there: {bootstrap}");
            return 1;
        }

        if (payload is null)
        {
            Console.Error.WriteLine($"The proxy payload ({string.Join(" or ", ProxyInstall.PayloadNames)}) was not found next to {AppContext.BaseDirectory}");
            return 1;
        }

        var known = ProxyInstall.KnownDllEntryForVersion();
        if (known is not null)
        {
            Console.Error.WriteLine($"This machine pins VERSION.dll in KnownDLLs ({known}), so the loader would take the system copy and the proxy would never run. Nothing was installed.");
            return 1;
        }

        var install = ProxyInstall.Install(gameDirectory, launcherDirectory, payload, args.Contains("--force"));
        if (!install.Ok)
        {
            Console.Error.WriteLine(install.Message);
            return 1;
        }

        Console.WriteLine($"Installed the proxy: {ProxyInstall.InstalledPath(gameDirectory)} (copied from {payload}).");
        Console.WriteLine($"Wrote {ProxyInstall.PointPath(gameDirectory)} naming this launcher directory: {launcherDirectory}");
        Console.WriteLine($"Logs: {Path.Combine(launcherDirectory, "bootstrap.log")}, {Path.Combine(launcherDirectory, "host.log")}, and {Path.Combine(launcherDirectory, "proxy.log")} (the proxy logs to %TEMP%\\Mystia.Proxy.log until it finds this directory).");

        Console.WriteLine($"Starting the game through Steam: steam://run/{config.SteamAppId}");
        try
        {
            Process.Start(new ProcessStartInfo($"steam://run/{config.SteamAppId}") { UseShellExecute = true });
        }
        catch (Exception error) when (error is Win32Exception or InvalidOperationException)
        {
            Console.Error.WriteLine($"Steam did not accept steam://run/{config.SteamAppId}: {error.Message}");
            return 1;
        }

        return 0;
    }

    private static string? ValueOf(string[] args, string flag)
    {
        var index = Array.IndexOf(args, flag);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    private sealed class LauncherFile
    {
        public string GameExe { get; set; } = "";

        public string ModsDirectory { get; set; } = "mods";

        public string ExpectedGameAssemblySha256 { get; set; } = "";

        public int SteamAppId { get; set; }
    }
}
