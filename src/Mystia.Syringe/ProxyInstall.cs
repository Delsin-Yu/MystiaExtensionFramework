using System.Text;
using Microsoft.Win32;

namespace Mystia.Syringe;

// The proxy is a drop-in replacement for a DLL the game already imports: native/Mystia.Proxy builds
// Mystia.Proxy.dll, and this installer copies it into the game directory as version.dll because
// UnityPlayer.dll statically imports VERSION.dll. Steam stays the launcher, so the game's own
// RestartAppIfNecessary call sees the launch it expects. The file contract in
// native/Mystia.Proxy/proxy.cpp must not change: Mystia.Proxy.txt holds this launcher directory and
// the proxy loads Mystia.Bootstrap.dll from it.
internal static class ProxyInstall
{
    public const string InstalledName = "version.dll";
    public const string PointFileName = "Mystia.Proxy.txt";

    // The native build output name comes first, so the launcher directory never holds a literal
    // version.dll: a launcher process that imports VERSION.dll would otherwise hijack itself the same
    // way the game is hijacked. version.dll stays accepted for a deployment that already renames it.
    public static readonly string[] PayloadNames = ["Mystia.Proxy.dll", "version.dll"];

    public static string InstalledPath(string gameDirectory) => Path.Combine(gameDirectory, InstalledName);

    public static string PointPath(string gameDirectory) => Path.Combine(gameDirectory, PointFileName);

    public static string? FindPayload(string launcherDirectory) =>
        PayloadNames.Select(name => Path.Combine(launcherDirectory, name)).FirstOrDefault(File.Exists);

    // Returns the KnownDLLs entry that pins VERSION.dll, or null. A KnownDLL is resolved from the
    // system directory before any application directory, so an installed proxy would never be called.
    public static string? KnownDllEntryForVersion()
    {
        if (!OperatingSystem.IsWindows())
            return null;

        using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Session Manager\KnownDLLs");
        if (key is null)
            return null;

        foreach (var name in key.GetValueNames())
        {
            if (IsVersion(name))
                return name;
            if (key.GetValue(name) is string value && IsVersion(value))
                return value;
        }

        return null;
    }

    private static bool IsVersion(string entry)
    {
        var bare = entry.TrimStart('*', '_');
        if (bare.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            bare = bare[..^4];
        return string.Equals(bare, "version", StringComparison.OrdinalIgnoreCase);
    }

    public static bool SameBytes(string left, string right)
    {
        if (new FileInfo(left).Length != new FileInfo(right).Length)
            return false;
        return File.ReadAllBytes(left).AsSpan().SequenceEqual(File.ReadAllBytes(right));
    }

    public static ProxyResult Install(string gameDirectory, string launcherDirectory, string payload, bool force)
    {
        var target = InstalledPath(gameDirectory);
        if (File.Exists(target) && !force && !SameBytes(target, payload))
        {
            return new ProxyResult(
                false,
                $"'{target}' already exists and is not our proxy, so another loader may own it. Nothing was installed; pass --force to overwrite it.");
        }

        if (!string.Equals(Path.GetFullPath(payload), Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase))
            File.Copy(payload, target, true);
        File.WriteAllText(PointPath(gameDirectory), launcherDirectory + Environment.NewLine, new UTF8Encoding(false));
        return new ProxyResult(true, "");
    }

    public static ProxyResult Uninstall(string gameDirectory, string launcherDirectory, string? payload)
    {
        var lines = new List<string>();

        var target = InstalledPath(gameDirectory);
        if (!File.Exists(target))
            lines.Add($"not installed here: {target}");
        else if (payload is null)
            lines.Add($"kept {target}: the proxy payload is not next to the launcher, so this file cannot be identified as ours");
        else if (!SameBytes(target, payload))
            lines.Add($"kept {target}: it is not our proxy");
        else
        {
            File.Delete(target);
            lines.Add($"removed {target}");
        }

        var point = PointPath(gameDirectory);
        if (!File.Exists(point))
            lines.Add($"not installed here: {point}");
        else if (!string.Equals(File.ReadAllText(point).Trim(), launcherDirectory, StringComparison.OrdinalIgnoreCase))
            lines.Add($"kept {point}: it names another launcher directory");
        else
        {
            File.Delete(point);
            lines.Add($"removed {point}");
        }

        return new ProxyResult(true, string.Join(Environment.NewLine, lines));
    }
}

internal readonly record struct ProxyResult(bool Ok, string Message);
