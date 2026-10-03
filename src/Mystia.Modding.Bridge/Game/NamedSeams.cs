using System.Reflection;
using System.Runtime.InteropServices;
using HarmonyLib;
using Il2CppInterop.Runtime;

namespace Mystia.Modding.Bridge;

/// <summary>
/// Locating a game member by name at startup, for the seams whose landing point is a compiler generated member.
/// Such a name only holds its number for one build: the compiler generated local functions of one signature are
/// numbered by their order in the game's own class, so the interop member a seam knows by name may answer with a
/// different local function of the same shape after a game update. A seam therefore resolves its member and
/// compares the name the IL2CPP runtime knows with the name the game's own source gives it, instead of trusting
/// the interop name alone.
/// </summary>
internal static class NamedSeams
{
    /// <summary>
    /// Resolves <paramref name="memberName"/> on <paramref name="declaringType"/> and refuses it unless
    /// <paramref name="nativeNameOf"/> reports exactly <paramref name="nativeName"/> for it. The reader is a
    /// parameter so the check itself is testable without a running game; <see cref="LocateIl2Cpp"/> supplies the
    /// runtime one.
    /// </summary>
    internal static MethodInfo Locate(
        Type declaringType,
        string memberName,
        string nativeName,
        Func<MethodInfo, string?> nativeNameOf)
    {
        var method = AccessTools.Method(declaringType, memberName)
            ?? throw new MissingMethodException(declaringType.FullName, memberName);
        var actual = nativeNameOf(method);
        if (actual is null)
            throw new MissingMethodException(
                $"{declaringType.FullName}::{memberName} has no IL2CPP method to check against '{nativeName}'.");
        if (!string.Equals(actual, nativeName, StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"{declaringType.FullName}::{memberName} is the IL2CPP method '{actual}', not '{nativeName}', so the seam would patch a different member.");
        return method;
    }

    /// <summary>The same check against the running game; needs the IL2CPP runtime, so it is patch time only.</summary>
    internal static MethodInfo LocateIl2Cpp(Type declaringType, string memberName, string nativeName) =>
        Locate(declaringType, memberName, nativeName, Il2CppName);

    /// <summary>
    /// The name the IL2CPP runtime knows a generated interop member by. Every generated member invokes through
    /// its own <c>NativeMethodInfoPtr_&lt;interop name&gt;</c> field, which holds the MethodInfo* the generator
    /// looked up under the game's own name, so that name can be read back from it.
    /// </summary>
    private static string? Il2CppName(MethodInfo method)
    {
        var field = method.DeclaringType?.GetField(
            "NativeMethodInfoPtr_" + method.Name,
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        if (field?.GetValue(null) is not nint pointer || pointer == nint.Zero)
            return null;
        return Marshal.PtrToStringAnsi(IL2CPP.il2cpp_method_get_name(pointer));
    }
}
