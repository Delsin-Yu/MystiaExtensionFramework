namespace Mystia.Modding.Bridge;

/// <summary>Which mod contributed an entry: every injected row is tagged with the owner's own id.</summary>
internal static class ContentOrigin
{
    private static readonly Dictionary<object, string> Origins = new(ReferenceEqualityComparer.Instance);

    internal static void Bind(object contributor, string origin)
    {
        if (!string.IsNullOrEmpty(origin))
            Origins[contributor] = origin;
    }

    internal static string Of(object contributor) =>
        Origins.TryGetValue(contributor, out var origin) ? origin : "";
}
