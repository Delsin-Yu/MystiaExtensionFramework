namespace Mystia;

/// <summary>
/// Called once per loaded mod, after its own registration and before the first scene runs. Everything the
/// mod registers itself is already in the registry when this runs.
/// </summary>
[AutoWire]
public interface IInitialization
{
    void Initialize(IMod mod);
}
