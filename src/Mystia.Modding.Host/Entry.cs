using System.Runtime.InteropServices;
using Mystia.Modding.Bridge;

namespace Mystia.Modding.Host;

internal static class Entry
{
    [UnmanagedCallersOnly(EntryPoint = "MystiaHostInitialize")]
    public static void MystiaHostInitialize(nint launcherDirectoryUtf16)
    {
        try
        {
            var directory = Marshal.PtrToStringUni(launcherDirectoryUtf16);
            if (string.IsNullOrWhiteSpace(directory))
                return;
            Start(directory);
        }
        catch (Exception error)
        {
            try
            {
                var directory = Marshal.PtrToStringUni(launcherDirectoryUtf16);
                if (!string.IsNullOrWhiteSpace(directory))
                    File.AppendAllText(Path.Combine(directory, "host.log"), error + Environment.NewLine);
            }
            catch
            {
                // The game process has no console. Losing the secondary log must not tear the process down.
            }
        }
    }

    public static void Start(string launcherDirectory)
    {
        var logPath = Path.Combine(launcherDirectory, "host.log");
        void Write(string line) => File.AppendAllText(logPath, DateTime.Now.ToString("HH:mm:ss.fff ") + line + Environment.NewLine);

        try
        {
            Write("Host entry.");
            var configPath = Path.Combine(launcherDirectory, "launcher.json");
            var config = LauncherConfig.Read(configPath);
            Write("Read launcher.json.");
            var modsDirectory = Path.IsPathRooted(config.ModsDirectory)
                ? config.ModsDirectory
                : Path.Combine(launcherDirectory, config.ModsDirectory);
            var gameRoot = Path.GetDirectoryName(config.GameExe) ?? "";
            var mainThread = new Mystia.Modding.Bridge.QueuedMainThread();
            var components = new BridgeComponents();
            var context = new HostContext(gameRoot, modsDirectory, new TextLog(Write), mainThread, components);
            Write("Installing the bridge.");
            BridgeInstaller.Install(mainThread, components, gameRoot);
            Write("Loading mods.");
            var registry = ModLoader.Load(modsDirectory, context, config.ModOrder, Write);
            BridgeInstaller.Bind(registry);
            Write($"Loaded mods from '{modsDirectory}'.");
        }
        catch (Exception error)
        {
            Write(error.ToString());
            throw;
        }
    }
}

internal sealed class LauncherConfig
{
    public string GameExe { get; set; } = "";

    public string ModsDirectory { get; set; } = "mods";

    public string ExpectedGameAssemblySha256 { get; set; } = "";

    public string[] ModOrder { get; set; } = [];

    public static LauncherConfig Read(string path)
    {
        var json = File.ReadAllText(path);
        return System.Text.Json.JsonSerializer.Deserialize<LauncherConfig>(json, new System.Text.Json.JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
        }) ?? throw new InvalidOperationException($"Could not read '{path}'.");
    }
}
