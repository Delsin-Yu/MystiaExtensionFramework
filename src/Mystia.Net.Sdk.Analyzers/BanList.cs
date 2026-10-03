using System;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;

namespace Mystia.Analyzers;

/// <summary>
/// The ban classification shared by every rule of <see cref="BannedModCodeAnalyzer"/>.
/// <para>
/// The framework is the only injection pipeline: a mod declares types that implement the SDK
/// interfaces and the host does everything else (interop startup, component injection, detours,
/// scene and guest seams). Code that reaches around that pipeline fights the host, breaks when the
/// game is updated, and cannot be supported. Each list below therefore names a dependency or a
/// namespace whose <em>use from mod source</em> is an error.
/// </para>
/// </summary>
internal static class BanList
{
    /// <summary>
    /// Compiler generated member names that Il2CppInterop sanitises into valid C# identifiers
    /// (<c>&lt;MainChallengeLoop&gt;d__16</c> becomes a name containing <c>_d__16</c>).
    /// They are not public API: the names carry the private layout of one exact game build, so a
    /// mod that names one of them breaks on the next update. Mods must stay on the SDK surface.
    /// </summary>
    private static readonly Regex CompilerGeneratedName = new(
        "(__c__DisplayClass|ObjectCompilerGenerated|Method_Internal_|field_Public_|_d__[0-9]|_PDM_[0-9])",
        RegexOptions.CultureInvariant);

    /// <summary>
    /// Assemblies a mod must not take a dependency on. The framework owns injection, so shipping
    /// Harmony, BepInEx or MonoMod either double-patches the game or bypasses the host entirely.
    /// </summary>
    private static readonly string[] BannedAssemblyPrefixes =
    {
        "0Harmony",
        "HarmonyLib",
        "BepInEx",
        "MonoMod",
    };

    /// <summary>True when <paramref name="identifier"/> carries a compiler generated member name.</summary>
    internal static bool IsCompilerGeneratedName(string identifier) => CompilerGeneratedName.IsMatch(identifier);

    /// <summary>True when the assembly is one a mod may not reference.</summary>
    internal static bool IsBannedAssembly(string? assemblyName)
    {
        if (assemblyName is null || assemblyName.Length == 0)
            return false;

        foreach (var prefix in BannedAssemblyPrefixes)
        {
            if (assemblyName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    /// <summary>True when <paramref name="namespaceName"/> is <paramref name="root"/> or a child of it.</summary>
    internal static bool IsSameOrChildNamespace(string namespaceName, string root) =>
        string.Equals(namespaceName, root, StringComparison.Ordinal)
        || (namespaceName.Length > root.Length
            && namespaceName[root.Length] == '.'
            && namespaceName.StartsWith(root, StringComparison.Ordinal));

    /// <summary>Classifies a namespace name, or returns <see langword="null"/> when it is allowed.</summary>
    internal static DiagnosticDescriptor? ClassifyNamespace(string? namespaceName)
    {
        if (namespaceName is null || namespaceName.Length == 0)
            return null;

        if (IsSameOrChildNamespace(namespaceName, "Il2CppInterop.Runtime.Injection")
            || IsSameOrChildNamespace(namespaceName, "Il2CppInterop.Runtime.Startup"))
            return BannedModCodeAnalyzer.Il2CppInjection;

        if (IsSameOrChildNamespace(namespaceName, "System.Reflection"))
            return BannedModCodeAnalyzer.ReflectionEscape;

        if (IsSameOrChildNamespace(namespaceName, "UnityEngine"))
            return BannedModCodeAnalyzer.UnityType;

        if (IsSameOrChildNamespace(namespaceName, "HarmonyLib")
            || IsSameOrChildNamespace(namespaceName, "BepInEx")
            || IsSameOrChildNamespace(namespaceName, "MonoMod"))
            return BannedModCodeAnalyzer.BannedDependency;

        return null;
    }

    /// <summary>Classifies a type, or returns <see langword="null"/> when mod code may use it.</summary>
    internal static DiagnosticDescriptor? ClassifyType(INamedTypeSymbol type)
    {
        if (IsType(type, "Il2CppInterop.Runtime", "IL2CPP"))
            return BannedModCodeAnalyzer.Il2CppInjection;

        if (IsType(type, "System.Runtime.InteropServices", "Marshal"))
            return BannedModCodeAnalyzer.ReflectionEscape;

        if (IsType(type, "System.Runtime.CompilerServices", "ModuleInitializerAttribute"))
            return BannedModCodeAnalyzer.ModuleInitializer;

        var byNamespace = ClassifyNamespace(NamespaceOf(type));
        if (byNamespace is not null)
            return byNamespace;

        // A type whose namespace is neutral but whose assembly is banned is still a banned dependency.
        if (IsBannedAssembly(type.ContainingAssembly?.Name))
            return BannedModCodeAnalyzer.BannedDependency;

        return null;
    }

    /// <summary>The full name of the namespace that directly contains the type, or an empty string.</summary>
    internal static string NamespaceOf(INamedTypeSymbol type)
    {
        var ns = type.ContainingNamespace;
        if (ns is null || ns.IsGlobalNamespace)
            return string.Empty;

        return ns.ToDisplayString();
    }

    private static bool IsType(INamedTypeSymbol type, string namespaceName, string typeName) =>
        string.Equals(type.Name, typeName, StringComparison.Ordinal)
        && string.Equals(NamespaceOf(type), namespaceName, StringComparison.Ordinal);
}
