namespace Mystia;

/// <summary>
/// The platform (store front) keys the host resolved at startup. A mod reads it after
/// <see cref="IInitialization"/>; while <see cref="KeysResolved"/> is false the platform has not
/// finished resolving and <see cref="ActiveDlcKeys"/> is empty.
/// </summary>
public interface IPlatformInfo
{
    bool KeysResolved { get; }

    /// <summary>Keys of the DLC active on this machine, in the platform's own key names.</summary>
    IReadOnlyList<string> ActiveDlcKeys { get; }
}
