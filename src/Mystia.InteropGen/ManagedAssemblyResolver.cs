using AsmResolver.DotNet;
using AsmResolver.DotNet.Serialized;

namespace Mystia.InteropGen;

sealed class ManagedAssemblyResolver : AssemblyResolverBase
{
    private readonly Dictionary<string, AssemblyDefinition> _loaded = new(StringComparer.OrdinalIgnoreCase);

    public ManagedAssemblyResolver(string directory)
        : base(new ModuleReaderParameters(directory))
    {
        SearchDirectories.Add(directory);
    }

    public void Remember(AssemblyDefinition assembly)
    {
        var name = ((AssemblyDescriptor)assembly).Name?.ToString();
        if (!string.IsNullOrEmpty(name))
            _loaded[name] = assembly;
    }

    protected override AssemblyDefinition? ResolveImpl(AssemblyDescriptor assembly)
    {
        var name = assembly.Name?.ToString();
        if (name is not null && _loaded.TryGetValue(name, out var known))
            return known;

        var path = ProbeSearchDirectories(assembly);
        if (path is null)
            return null;

        var loaded = LoadAssemblyFromFile(path);
        if (loaded is not null)
            Remember(loaded);
        return loaded;
    }

    protected override string? ProbeRuntimeDirectories(AssemblyDescriptor assembly) => null;
}
