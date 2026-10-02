using System.Text.Json;

namespace Mystia.Syringe;

public static class Program
{
    public static int Main(string[] args)
    {
        var launcherDirectory = AppContext.BaseDirectory;
        var configPath = args.Length >= 2 && args[0] == "--config"
            ? args[1]
            : Path.Combine(launcherDirectory, "launcher.json");
        if (!File.Exists(configPath))
        {
            Console.Error.WriteLine($"launcher.json was not found: {configPath}");
            return 1;
        }

        var config = JsonSerializer.Deserialize<LauncherFile>(File.ReadAllText(configPath), new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
        });
        if (config is null || string.IsNullOrWhiteSpace(config.GameExe))
        {
            Console.Error.WriteLine("launcher.json is missing gameExe.");
            return 1;
        }

        var guard = GameAssemblyGuard.Check(config.GameExe, config.ExpectedGameAssemblySha256);
        if (!guard.Ok)
        {
            Console.Error.WriteLine(guard.Message);
            return 1;
        }

        var bootstrap = Path.Combine(launcherDirectory, "Mystia.Bootstrap.dll");
        if (!File.Exists(bootstrap))
        {
            Console.Error.WriteLine($"Bootstrap DLL was not found: {bootstrap}");
            return 1;
        }

        ProcessInjector.Start(config.GameExe, bootstrap);
        Console.WriteLine("Injected Mystia.Bootstrap.dll and resumed the game. The Steam install was not modified.");
        return 0;
    }

    private sealed class LauncherFile
    {
        public string GameExe { get; set; } = "";

        public string ModsDirectory { get; set; } = "mods";

        public string ExpectedGameAssemblySha256 { get; set; } = "";
    }
}
