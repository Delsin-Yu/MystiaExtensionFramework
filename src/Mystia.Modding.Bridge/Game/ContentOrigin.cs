namespace Mystia.Modding.Bridge;

internal static class ContentOrigin
{
    private static readonly Dictionary<object, string> Roots = new(ReferenceEqualityComparer.Instance);

    internal static void Bind(object contributor, string directory)
    {
        if (!string.IsNullOrEmpty(directory))
            Roots[contributor] = directory;
    }

    internal static string Of(object contributor) =>
        Roots.TryGetValue(contributor, out var root) ? root : "";
}
