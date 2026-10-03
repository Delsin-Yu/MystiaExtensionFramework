using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Mystia.Analyzers;
using Mystia.Modding.Host;
using Xunit;

namespace Mystia.Tests;

/// <summary>
/// Runs <see cref="BannedModCodeAnalyzer"/> in process against a <see cref="CSharpCompilation"/>.
/// Every banned dependency lives in a library that is compiled on the fly, so the analyzer sees
/// real symbols in a foreign assembly, exactly like a mod that references Harmony or the interop.
/// </summary>
public sealed class AnalyzerTests
{
    /// <summary>A library that stands in for everything a mod must not touch.</summary>
    private const string BannedLibrary = """
        namespace HarmonyLib
        {
            public class Harmony { }
            public static class AccessTools { public static void PatchAll() { } }
        }

        namespace BepInEx { public class Plugin { } }

        namespace MonoMod.Utils { public class Helper { } }

        namespace Il2CppInterop.Runtime
        {
            public static class IL2CPP { public static int Image; }
        }

        namespace Il2CppInterop.Runtime.Injection
        {
            public static class ClassInjector { public static void RegisterTypeInIl2Cpp<T>() { } }
        }

        namespace Il2CppInterop.Runtime.Startup
        {
            public static class Il2CppInteropRuntime { }
        }

        namespace UnityEngine { public class GameObject { } }

        namespace Game
        {
            public class Ops
            {
                public int field_Public_7;
                public int _d__12;
                public void Method_Internal_3() { }
            }
        }
        """;

    [Fact]
    public void HarmonyLibUsageReportsMystia1001()
    {
        var diagnostics = Analyze(
            """
            using HarmonyLib;

            namespace SampleMod
            {
                public sealed class Hook
                {
                    public Harmony? Instance;
                }
            }
            """,
            BannedLibrary);

        Assert.NotEmpty(diagnostics);
        Assert.All(diagnostics, diagnostic => Assert.Equal(BannedModCodeAnalyzer.BannedDependencyDiagnosticId, diagnostic.Id));
        Assert.All(diagnostics, diagnostic => Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity));
    }

    [Fact]
    public void BepInExMonoModAndAccessToolsReportMystia1001()
    {
        var bepInEx = Analyze(
            """
            using BepInEx;

            namespace SampleMod
            {
                public sealed class Plug : Plugin { }
            }
            """,
            BannedLibrary);

        var monoMod = Analyze(
            """
            namespace SampleMod
            {
                public sealed class Reach
                {
                    public MonoMod.Utils.Helper? Helper;
                }
            }
            """,
            BannedLibrary);

        var accessTools = Analyze(
            """
            using static HarmonyLib.AccessTools;

            namespace SampleMod
            {
                public static class Installer
                {
                    public static void Install() => PatchAll();
                }
            }
            """,
            BannedLibrary);

        Assert.Contains(bepInEx, diagnostic => diagnostic.Id == BannedModCodeAnalyzer.BannedDependencyDiagnosticId);
        Assert.Contains(monoMod, diagnostic => diagnostic.Id == BannedModCodeAnalyzer.BannedDependencyDiagnosticId);
        Assert.Contains(accessTools, diagnostic => diagnostic.Id == BannedModCodeAnalyzer.BannedDependencyDiagnosticId);
    }

    [Fact]
    public void ATypeFromABannedAssemblyReportsMystia1001()
    {
        // The namespace is neutral; only the assembly name gives the dependency away.
        var diagnostics = Analyze(
            """
            namespace SampleMod
            {
                public sealed class Reach
                {
                    public Vendor.Helper? Helper;
                }
            }
            """,
            "namespace Vendor { public class Helper { } }",
            "0Harmony");

        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == BannedModCodeAnalyzer.BannedDependencyDiagnosticId);
    }

    [Fact]
    public void Il2CppInteropInjectionReportsMystia1002()
    {
        var injection = Analyze(
            """
            using Il2CppInterop.Runtime.Injection;

            namespace SampleMod
            {
                public sealed class Component
                {
                    public static void Add() => ClassInjector.RegisterTypeInIl2Cpp<Component>();
                }
            }
            """,
            BannedLibrary);

        var startup = Analyze(
            """
            using Il2CppInterop.Runtime.Startup;

            namespace SampleMod
            {
                public sealed class Boot
                {
                    public Il2CppInteropRuntime? Runtime;
                }
            }
            """,
            BannedLibrary);

        var raw = Analyze(
            """
            namespace SampleMod
            {
                public sealed class Raw
                {
                    public int Image = Il2CppInterop.Runtime.IL2CPP.Image;
                }
            }
            """,
            BannedLibrary);

        Assert.Contains(injection, diagnostic => diagnostic.Id == BannedModCodeAnalyzer.Il2CppInjectionDiagnosticId);
        Assert.Contains(startup, diagnostic => diagnostic.Id == BannedModCodeAnalyzer.Il2CppInjectionDiagnosticId);
        Assert.Contains(raw, diagnostic => diagnostic.Id == BannedModCodeAnalyzer.Il2CppInjectionDiagnosticId);
    }

    [Fact]
    public void ReflectionEmitAndMarshalReportMystia1003()
    {
        var reflection = Analyze(
            """
            using System.Reflection;

            namespace SampleMod
            {
                public sealed class Reach
                {
                    public Assembly? Loaded;
                }
            }
            """);

        var emit = Analyze(
            """
            namespace SampleMod
            {
                public sealed class Reach
                {
                    public object? Type = typeof(System.Reflection.Emit.DynamicMethod);
                }
            }
            """);

        var marshal = Analyze(
            """
            namespace SampleMod
            {
                public sealed class Reach
                {
                    public int Error = System.Runtime.InteropServices.Marshal.GetLastWin32Error();
                }
            }
            """);

        Assert.Contains(reflection, diagnostic => diagnostic.Id == BannedModCodeAnalyzer.ReflectionEscapeDiagnosticId);
        Assert.Contains(emit, diagnostic => diagnostic.Id == BannedModCodeAnalyzer.ReflectionEscapeDiagnosticId);
        Assert.Contains(marshal, diagnostic => diagnostic.Id == BannedModCodeAnalyzer.ReflectionEscapeDiagnosticId);
    }

    [Fact]
    public void UnityEngineTypesReportMystia1004()
    {
        var diagnostics = Analyze(
            """
            using UnityEngine;

            namespace SampleMod
            {
                public sealed class Reach
                {
                    public GameObject? Object;
                }
            }
            """,
            BannedLibrary);

        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == BannedModCodeAnalyzer.UnityTypeDiagnosticId);
        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == BannedModCodeAnalyzer.BannedDependencyDiagnosticId);
    }

    [Fact]
    public void CompilerGeneratedMemberNamesReportMystia1005()
    {
        var usage = Analyze(
            """
            namespace SampleMod
            {
                public sealed class Reach
                {
                    public void Run(Game.Ops ops)
                    {
                        ops.Method_Internal_3();
                        var field = ops.field_Public_7;
                        var state = ops._d__12;
                    }
                }
            }
            """,
            BannedLibrary);

        var reported = usage
            .Where(diagnostic => diagnostic.Id == BannedModCodeAnalyzer.CompilerGeneratedDiagnosticId)
            .ToArray();
        Assert.Equal(3, reported.Length);
        Assert.Contains(reported, diagnostic => diagnostic.GetMessage().Contains("Method_Internal_3"));
        Assert.Contains(reported, diagnostic => diagnostic.GetMessage().Contains("field_Public_7"));
        Assert.Contains(reported, diagnostic => diagnostic.GetMessage().Contains("_d__12"));
    }

    [Fact]
    public void DeclaringCompilerGeneratedMemberNamesReportsMystia1005()
    {
        var diagnostics = Analyze(
            """
            namespace SampleMod
            {
                public class __c__DisplayClass1 { }

                public sealed class Reach
                {
                    private int field_Public_9;
                    private static int _PDM_4;

                    public void Run() { int ObjectCompilerGenerated5 = 0; }
                }
            }
            """);

        var reported = diagnostics
            .Where(diagnostic => diagnostic.Id == BannedModCodeAnalyzer.CompilerGeneratedDiagnosticId)
            .ToArray();
        Assert.Equal(4, reported.Length);
        Assert.Contains(reported, diagnostic => diagnostic.GetMessage().Contains("__c__DisplayClass1"));
        Assert.Contains(reported, diagnostic => diagnostic.GetMessage().Contains("ObjectCompilerGenerated5"));
    }

    [Fact]
    public void ModuleInitializerReportsMystia1006()
    {
        var diagnostics = Analyze(
            """
            using System.Runtime.CompilerServices;

            namespace SampleMod
            {
                internal static class Boot
                {
                    [ModuleInitializer]
                    internal static void Init() { }
                }
            }
            """);

        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == BannedModCodeAnalyzer.ModuleInitializerDiagnosticId);
    }

    [Fact]
    public void CompliantModCodeReportsNothing()
    {
        var diagnostics = Analyze(
            """
            using System.Collections.Generic;
            using Game;

            namespace SampleMod
            {
                public sealed class SceneLog
                {
                    private readonly List<int> _seen = new List<int>();

                    public void OnSceneStart(Ops ops) => _seen.Add(ops.Id);
                }
            }
            """,
            "namespace Game { public class Ops { public int Id; } }",
            "Assembly-CSharp");

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void OnlyTheOffendingFileReports()
    {
        var diagnostics = Analyze(
            [
                ("Mod.cs", "using HarmonyLib;\n\nnamespace SampleMod { public sealed class Hook { } }"),
                ("Clean.cs", "namespace SampleMod { public sealed class Other { public int Value; } }"),
            ],
            BannedLibrary);

        Assert.NotEmpty(diagnostics);
        Assert.All(diagnostics, diagnostic => Assert.Equal("Mod.cs", diagnostic.Location.GetLineSpan().Path));
    }

    [Fact]
    public void TheLoaderFlagsEveryBannedReference()
    {
        var references = new[]
            {
                "Mystia.Net.Sdk",
                "0Harmony",
                "HarmonyLib",
                "BepInEx.Core",
                "MonoPlus",
                "MonoMod.Utils",
                "Assembly-CSharp",
                "Il2CppInterop.Runtime",
            }
            .Select(name => new AssemblyName(name))
            .ToArray();

        var banned = ModLoader.FindBannedReferences(references);

        Assert.Equal(new[] { "0Harmony", "HarmonyLib", "BepInEx.Core", "MonoPlus", "MonoMod.Utils" }, banned);
    }

    /// <summary>
    /// Assemblies the test project pulls in through its own dependency graph (the host drags in
    /// Harmony, Il2CppInterop and the Unity interop). They would shadow the stand-in library and
    /// make every banned name ambiguous, so the compilation under test only sees the platform and
    /// the stand-in.
    /// </summary>
    private static readonly string[] ShadowingAssemblyPrefixes =
    [
        "0Harmony",
        "BepInEx",
        "HarmonyLib",
        "Il2Cpp",
        "MonoMod",
        "UnityEngine",
    ];

    private static readonly MetadataReference[] PlatformReferences = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
        .Split(Path.PathSeparator)
        .Where(path => !ShadowingAssemblyPrefixes.Any(
            prefix => Path.GetFileName(path).StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
        .Select(path => MetadataReference.CreateFromFile(path))
        .Cast<MetadataReference>()
        .ToArray();

    private static IReadOnlyList<Diagnostic> Analyze(
        string source,
        string? library = null,
        string libraryName = "BannedLibrary") =>
        Analyze([("Mod.cs", source)], library, libraryName);

    private static IReadOnlyList<Diagnostic> Analyze(
        (string Path, string Source)[] files,
        string? library = null,
        string libraryName = "BannedLibrary")
    {
        var references = new List<MetadataReference>(PlatformReferences);
        if (library is not null)
            references.Add(CompileLibrary(library, libraryName));

        var compilation = CSharpCompilation.Create(
            "ModUnderTest",
            files.Select(file => CSharpSyntaxTree.ParseText(file.Source, path: file.Path)),
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var withAnalyzers = compilation.WithAnalyzers(
            [new BannedModCodeAnalyzer()],
            new AnalyzerOptions([]));

        return withAnalyzers
            .GetAnalyzerDiagnosticsAsync(CancellationToken.None)
            .GetAwaiter()
            .GetResult()
            .OrderBy(diagnostic => diagnostic.Id, StringComparer.Ordinal)
            .ThenBy(diagnostic => diagnostic.Location.SourceSpan.Start)
            .ToArray();
    }

    private static MetadataReference CompileLibrary(string source, string assemblyName)
    {
        var compilation = CSharpCompilation.Create(
            assemblyName,
            [CSharpSyntaxTree.ParseText(source)],
            PlatformReferences,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        using var stream = new MemoryStream();
        var result = compilation.Emit(stream);
        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics));

        return MetadataReference.CreateFromImage(stream.ToArray());
    }
}
