using System.Text.Json;
using Mystia.Modding.Bridge;
using Mystia;
using UnityEngine;

namespace Mystia.Modding.Host;

internal static class ModLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static ModRegistry Load(string modsDirectory, IModContext context, IReadOnlyList<string>? modOrder, Action<string>? warn = null)
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
            var assembly = System.Runtime.Loader.AssemblyLoadContext.Default.LoadFromAssemblyPath(manifest.AssemblyPath);
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
            var modContext = new ScopedContext(context, manifest.Directory, context.Log.Tag(manifest.Id));
            foreach (var instance in registry.InstancesAddedSince(before))
            {
                ContentOrigin.Bind(instance, manifest.Directory);
                if (instance is IPostInitialize post)
                    post.PostInitialize(modContext);
            }
        }

        return registry;
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

    private sealed class ScopedContext(IModContext inner, string modDirectory, ILog log)
        : IModContext
    {
        public ILog Log { get; } = log;

        public IMainThreadScheduler MainThread => inner.MainThread;

        public IGamePaths Paths { get; } = new ScopedPaths(inner.Paths.GameRoot, modDirectory);

        public IIl2CppComponentHost Components => inner.Components;

        public Sprite LoadSprite(string path) => SpriteFiles.Load(Paths.ModDirectory, path);
    }

    private sealed class ScopedPaths(string gameRoot, string modDirectory)
        : IGamePaths
    {
        public string GameRoot { get; } = gameRoot;

        public string ModDirectory { get; } = modDirectory;
    }
}
