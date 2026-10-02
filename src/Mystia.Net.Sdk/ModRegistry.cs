namespace Mystia;

internal sealed class ModRegistry : IModRegistrar
{
    private readonly List<Registration> _items = [];
    private readonly HashSet<string> _keys = new(StringComparer.Ordinal);

    public void Add<TContract>(TContract instance) where TContract : class
    {
        ArgumentNullException.ThrowIfNull(instance);
        _items.Add(new Registration(typeof(TContract), instance));
    }

    public void Add<TContract>(string key, TContract instance) where TContract : class
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("A keyed registration needs a key.", nameof(key));
        var slot = typeof(TContract).FullName + ":" + key;
        if (!_keys.Add(slot))
            throw new InvalidOperationException($"Duplicate key '{key}' for {typeof(TContract).FullName}.");
        Add(instance);
    }

    public IReadOnlyList<TContract> GetInstances<TContract>()
    {
        var matches = new List<TContract>();
        foreach (var item in _items)
        {
            if (item.Contract == typeof(TContract) && item.Instance is TContract typed)
                matches.Add(typed);
        }

        return matches;
    }

    public IReadOnlyList<object> InstancesAddedSince(int previousCount)
    {
        var added = new List<object>();
        var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
        for (var index = previousCount; index < _items.Count; index++)
        {
            if (seen.Add(_items[index].Instance))
                added.Add(_items[index].Instance);
        }

        return added;
    }

    public int Count => _items.Count;

    private readonly record struct Registration(Type Contract, object Instance);
}
