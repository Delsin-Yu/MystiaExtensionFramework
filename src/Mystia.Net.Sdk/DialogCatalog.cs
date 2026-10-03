using GameData.Profile;

namespace Mystia;

/// <summary>Read access to the dialog packages the game currently knows about.</summary>
public interface IDialogCatalog
{
    IReadOnlyList<string> Names { get; }

    bool TryResolve(string name, out DialogPackage package);
}
