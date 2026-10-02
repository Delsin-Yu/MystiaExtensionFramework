namespace Mystia.Modding.Host;

internal static class ModOrder
{
    public static IReadOnlyList<ModManifest> Sort(IReadOnlyList<ModManifest> mods, IReadOnlyList<string>? modOrder)
    {
        var byId = new Dictionary<string, ModManifest>(StringComparer.Ordinal);
        foreach (var mod in mods)
        {
            if (string.IsNullOrWhiteSpace(mod.Id))
                throw new InvalidOperationException("A mod manifest is missing an id.");
            if (!byId.TryAdd(mod.Id, mod))
                throw new InvalidOperationException($"Duplicate mod id '{mod.Id}'.");
        }

        var sorted = new List<ModManifest>(mods.Count);
        var used = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in modOrder ?? [])
        {
            if (string.IsNullOrWhiteSpace(id) || !used.Add(id) || !byId.TryGetValue(id, out var manifest))
                continue;
            sorted.Add(manifest);
        }

        var rest = byId.Keys.Where(id => !used.Contains(id)).ToList();
        rest.Sort(StringComparer.Ordinal);
        foreach (var id in rest)
            sorted.Add(byId[id]);
        return sorted;
    }
}
