using Mystia.Scenes;
using UnityEngine;

namespace Mystia.Modding.Bridge;

/// <summary>
/// The framework's <see cref="IInputServices"/>. It wraps the engine's own input state, which is main thread
/// only, so a loop polls it from there.
/// </summary>
internal sealed class KeyboardServices : IInputServices
{
    internal static readonly KeyboardServices Shared = new();

    public bool IsKeyDown(MystiaKey key) => Input.GetKeyDown(Key(key));

    public bool IsKeyHeld(MystiaKey key) => Input.GetKey(Key(key));

    /// <summary>
    /// The engine key a framework key stands for. Every member of <see cref="MystiaKey"/> is named after the
    /// engine key it maps to, so the mapping is the identity by name; an unmapped value answers the engine's
    /// own "no key", which is never down.
    /// </summary>
    internal static KeyCode Key(MystiaKey key) => key switch
    {
        MystiaKey.RightShift => KeyCode.RightShift,
        MystiaKey.Backslash => KeyCode.Backslash,
        MystiaKey.Slash => KeyCode.Slash,
        MystiaKey.T => KeyCode.T,
        MystiaKey.F1 => KeyCode.F1,
        MystiaKey.F2 => KeyCode.F2,
        MystiaKey.F3 => KeyCode.F3,
        MystiaKey.Return => KeyCode.Return,
        MystiaKey.KeypadEnter => KeyCode.KeypadEnter,
        MystiaKey.Escape => KeyCode.Escape,
        _ => KeyCode.None,
    };
}
