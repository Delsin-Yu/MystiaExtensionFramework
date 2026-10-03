using GameData.Profile;
using GameData.Core.Collections.DaySceneUtility;

namespace Mystia.Modding.Bridge;

internal sealed class DialogCatalog : IDialogCatalog
{
    internal static readonly DialogCatalog Shared = new();

    public IReadOnlyList<string> Names
    {
        get
        {
            var packages = DataBaseDay.allDialogPackages;
            if (packages is null || packages.Count == 0)
                return [];

            var names = new List<string>(packages.Count);
            foreach (var name in packages.Keys)
                names.Add(name);
            return names;
        }
    }

    public bool TryResolve(string name, out DialogPackage package)
    {
        package = null!;
        if (string.IsNullOrEmpty(name))
            return false;

        var packages = DataBaseDay.allDialogPackages;
        if (packages is null || !packages.ContainsKey(name))
            return false;

        package = packages[name];
        return package is not null;
    }
}
