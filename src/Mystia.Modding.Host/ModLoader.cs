using System.Text.Json;
using Mystia.Modding.Bridge;
using Mystia;

namespace Mystia.Modding.Host;

internal static class ModLoader
{
    /// <summary>
    /// Assemblies a mod must not reference. The framework is the only injection pipeline, so a mod
    /// that brings its own patcher either double-patches the game or bypasses the host's ordering.
    /// The Roslyn analyzer (MYSTIA1001) rejects these at compile time; this list catches mods that
    /// were built before the analyzer existed or outside the SDK.
    /// </summary>
    private static readonly string[] BannedAssemblyPrefixes =
    [
        "0Harmony",
        "HarmonyLib",
        "BepInEx",
        "MonoPlus",
        "MonoMod",
    ];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static ModRegistry Load(string modsDirectory, IMod host, IReadOnlyList<string>? modOrder, Action<string>? warn = null)
    {
        var registry = new ModRegistry();
        if (!Directory.Exists(modsDirectory))
            return registry;

        var manifests = new List<ModManifest>();
        foreach (var directory in Directory.GetDirectories(modsDirectory))
        {
            var manifestPath = Path.Combine(directory, "mod.json");
            if (!File.Exists(manifestPath))
                continue;
            var manifest = JsonSerializer.Deserialize<ModManifest>(File.ReadAllText(manifestPath), JsonOptions)
                ?? throw new InvalidOperationException($"Could not read '{manifestPath}'.");
            manifest.Directory = directory;
            manifest.AssemblyPath = ResolveAssembly(directory, manifest);
            manifests.Add(manifest);
        }

        foreach (var manifest in ModOrder.Sort(manifests, modOrder))
        {
            // The mod's own directory joins the probe list before its assembly is loaded, so a dependency
            // shipped beside the mod is found by the time the mod's own code binds it.
            ModAssemblyResolver.Add(manifest.Directory);
            try
            {
                var assembly = System.Runtime.Loader.AssemblyLoadContext.Default.LoadFromAssemblyPath(manifest.AssemblyPath);
                WarnOnBannedReferences(assembly, manifest, warn);
                var entrance = assembly.GetCustomAttributes(typeof(ModEntranceAttribute), inherit: false)
                    .OfType<ModEntranceAttribute>()
                    .SingleOrDefault();
                if (entrance is null)
                {
                    warn?.Invoke($"Mod '{manifest.Id}' has no generated entrance. The Mystia SDK generator did not run.");
                    continue;
                }

                var register = entrance.EntranceType.GetMethod(
                    "Register",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static,
                    binder: null,
                    types: [typeof(IModRegistrar)],
                    modifiers: null);
                if (register is null)
                    throw new InvalidOperationException($"Mod '{manifest.Id}' entrance is missing Register.");

                var before = registry.Count;
                register.Invoke(null, [registry]);
                var mod = new ScopedContext(host, manifest.Directory, manifest.Id, manifest.Version);
                foreach (var instance in registry.InstancesAddedSince(before))
                {
                    // The origin is bound first: everything the bridge later asks "which mod is this?" about,
                    // including the save handlers, resolves it here.
                    ContentOrigin.Bind(instance, manifest.Id);
                    if (instance is IInitialization initialization)
                        initialization.Initialize(mod);
                }
            }
            catch (Exception error)
            {
                // A mod that cannot load is the player's to see and fix, not the host's to die of: the mods
                // beside it keep loading.
                warn?.Invoke($"Mod '{manifest.Id}' failed to load and was skipped. {error}");
            }
        }

        return registry;
    }

    /// <summary>
    /// Returns the names in <paramref name="references"/> that the ban list blocks, in the order the
    /// assembly declared them. Used both by the load-time warning and by the tests.
    /// </summary>
    internal static IReadOnlyList<string> FindBannedReferences(IEnumerable<System.Reflection.AssemblyName> references)
    {
        var hits = new List<string>();
        foreach (var reference in references)
        {
            var name = reference.Name;
            if (string.IsNullOrEmpty(name))
                continue;
            foreach (var prefix in BannedAssemblyPrefixes)
            {
                if (!name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    continue;
                hits.Add(name);
                break;
            }
        }

        return hits;
    }

    /// <summary>
    /// Warns about every banned assembly the mod references and keeps loading it: a mod that ships
    /// its own patcher usually still works, it just fights the host, and the player has to see why.
    /// </summary>
    private static void WarnOnBannedReferences(System.Reflection.Assembly assembly, ModManifest manifest, Action<string>? warn)
    {
        foreach (var name in FindBannedReferences(assembly.GetReferencedAssemblies()))
        {
            warn?.Invoke(
                $"Mod '{manifest.Id}' references the banned assembly '{name}'. " +
                "The framework is the only injection pipeline, so this mod conflicts with the host.");
        }
    }

    private static string ResolveAssembly(string directory, ModManifest manifest)
    {
        if (!string.IsNullOrWhiteSpace(manifest.Assembly))
        {
            var specified = Path.Combine(directory, manifest.Assembly);
            if (!File.Exists(specified))
                throw new FileNotFoundException($"Mod '{manifest.Id}' assembly was not found.", specified);
            return Path.GetFullPath(specified);
        }

        var candidates = Directory.GetFiles(directory, "*.dll")
            .Where(path => !string.Equals(Path.GetFileName(path), "Mystia.Net.Sdk.dll", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (candidates.Length != 1)
            throw new InvalidOperationException(
                $"Mod '{manifest.Id}' has {candidates.Length} assemblies. Set \"assembly\" in mod.json.");
        return Path.GetFullPath(candidates[0]);
    }

    private sealed class ScopedContext(IMod host, string modDirectory, string modId, string modVersion)
        : IMod
    {
        public string Id { get; } = modId;

        public string Version { get; } = modVersion;

        public string Directory { get; } = modDirectory;

        public ILog Log { get; } = new ModLog(host.Log, modId, modVersion);

        public IModStorage Storage { get; } = new ModStorage(modDirectory);
    }

    /// <summary>Adds the mod's own identity to any log it receives from the host.</summary>
    private sealed class ModLog(ILog inner, string id, string version) : ILog
    {
        public string Id { get; } = id;

        public string Version { get; } = version;

        public void Debug(string message) => inner.Debug(message);

        public void Info(string message) => inner.Info(message);

        public void Message(string message) => inner.Message(message);

        public void Warning(string message) => inner.Warning(message);

        public void Error(string message) => inner.Error(message);

        public void Fatal(string message) => inner.Fatal(message);

        public void Log(LogLevel level, string message) => inner.Log(level, message);

        public ILog Tag(string tag) => new ModLog(inner.Tag(tag), Id, Version);
    }
}
