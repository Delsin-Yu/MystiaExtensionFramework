using System.Reflection;
using System.Runtime.Loader;

namespace Mystia.Modding.Host;

/// <summary>
/// A mod's assembly is loaded into the default context, because its types have to stay shared with the
/// bridge and the SDK. That context only probes the host's own directory though, so a dependency shipped
/// next to the mod is never found and the mod cannot bind its own code. The host therefore remembers the
/// mod directories it has loaded and answers the default context's unresolved-assembly callback from them.
/// </summary>
internal static class ModAssemblyResolver
{
    private static readonly object Gate = new();
    private static string[] _directories = [];
    private static bool _hooked;

    /// <summary>
    /// Adds <paramref name="directory"/> to the probe list. Directories keep the order they were added
    /// in, so the first mod that ships a copy of a shared dependency is the mod that resolves it.
    /// </summary>
    internal static void Add(string directory)
    {
        var full = Path.GetFullPath(directory);
        lock (Gate)
        {
            if (_directories.Contains(full, StringComparer.OrdinalIgnoreCase))
                return;
            _directories = [.. _directories, full];
            if (_hooked)
                return;
            _hooked = true;
        }

        AssemblyLoadContext.Default.Resolving += Resolve;
    }

    /// <summary>
    /// The files that could hold <paramref name="assemblyName"/>, in probe order: every directory that has
    /// one, and nothing at all for a name that is empty. It reads the file system but no load context, so
    /// the order a mod's dependency would be taken from is checkable without loading anything.
    /// </summary>
    internal static IReadOnlyList<string> Candidates(IReadOnlyList<string> directories, string? assemblyName)
    {
        var candidates = new List<string>();
        if (string.IsNullOrEmpty(assemblyName))
            return candidates;
        foreach (var directory in directories)
        {
            var path = Path.Combine(directory, assemblyName + ".dll");
            if (File.Exists(path))
                candidates.Add(path);
        }

        return candidates;
    }

    /// <summary>
    /// Answers a name the default context could not resolve itself, so an assembly the process already
    /// knows is still the one it gets. The first directory that has the file wins.
    /// </summary>
    private static Assembly? Resolve(AssemblyLoadContext context, AssemblyName name)
    {
        string[] directories;
        lock (Gate)
            directories = _directories;

        var candidates = Candidates(directories, name.Name);
        return candidates.Count == 0 ? null : context.LoadFromAssemblyPath(candidates[0]);
    }
}
