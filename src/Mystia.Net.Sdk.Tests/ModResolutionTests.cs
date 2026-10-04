using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Mystia.Modding.Host;
using Mystia;
using Xunit;

namespace Mystia.Tests;

/// <summary>
/// A mod that ships dependency assemblies beside its own. The host loads the mod into the default context
/// — its types have to stay shared with the bridge and the SDK — and that context does not probe the mod's
/// directory, so the host has to answer the miss itself.
/// </summary>
public sealed class ModResolutionTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "mystia-mod-resolution-" + Guid.NewGuid().ToString("N"));

    public ModResolutionTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // The loaded assemblies keep their files open. A leftover temporary directory is not a test
            // failure.
        }
    }

    [Fact]
    public void ModDirectoriesAreProbedInTheOrderTheyWereRegistered()
    {
        var first = Path.Combine(_root, "first");
        var second = Path.Combine(_root, "second");
        var empty = Path.Combine(_root, "empty");
        Directory.CreateDirectory(first);
        Directory.CreateDirectory(second);
        Directory.CreateDirectory(empty);
        File.WriteAllBytes(Path.Combine(first, "Sample.Dep.dll"), [1]);
        File.WriteAllBytes(Path.Combine(second, "Sample.Dep.dll"), [2]);

        var candidates = ModAssemblyResolver.Candidates([first, empty, second], "Sample.Dep");

        Assert.Equal(
            new[] { Path.Combine(first, "Sample.Dep.dll"), Path.Combine(second, "Sample.Dep.dll") },
            candidates);
        // The name picks the file: a directory that holds no such file is no candidate, and neither is a
        // request that carries no name at all.
        Assert.Empty(ModAssemblyResolver.Candidates([first], "Sample.Absent"));
        Assert.Empty(ModAssemblyResolver.Candidates([first], ""));
        Assert.Empty(ModAssemblyResolver.Candidates([first], null));
    }

    [Fact]
    public void AModThatShipsItsDependencyBesideItLoadsAndRuns()
    {
        // The names are unique per run: the loaded assemblies stay in the test process for good, and a
        // second test must not be handed the first one's copy.
        var suffix = Guid.NewGuid().ToString("N");
        var dependencyName = "SampleDependency" + suffix;
        var modName = "SampleMod" + suffix;
        var modDirectory = Path.Combine(_root, modName);
        Directory.CreateDirectory(modDirectory);

        var dependency = Compile(
            dependencyName,
            $$"""
            namespace {{dependencyName}}
            {
                public sealed class Marker
                {
                    public string Text => "the dependency ran";
                }
            }
            """,
            modDirectory);
        // The dependency sits beside the mod and nowhere the default context probes on its own.
        Assert.Equal(modDirectory, Path.GetDirectoryName(dependency));

        var mod = Compile(
            modName,
            $$"""
            using Mystia;

            [assembly: ModEntrance(typeof(SampleMod.Entrance))]

            namespace SampleMod
            {
                public static class Entrance
                {
                    public static void Register(IModRegistrar registrar) =>
                        registrar.Add<IInitialization>(new Probe(new {{dependencyName}}.Marker().Text));
                }

                public sealed class Probe : IInitialization
                {
                    private readonly string _text;

                    public Probe(string text) => _text = text;

                    public void Initialize(IMod mod) => mod.Log.Info(_text);
                }
            }
            """,
            modDirectory,
            dependency,
            typeof(AutoWireAttribute).Assembly.Location);

        File.WriteAllText(
            Path.Combine(modDirectory, "mod.json"),
            "{\"id\":\"sample.dependency\",\"version\":\"1.0.0\",\"assembly\":\"" + modName + ".dll\"}");

        var lines = new List<string>();
        var registry = ModLoader.Load(_root, new HostContext(_root, new TextLog(lines.Add)), null, lines.Add);

        // The mod's own code ran, and the value it logged is what only the dependency could produce.
        Assert.Single(registry.GetInstances<IInitialization>());
        Assert.Contains(lines, line => line.Contains("the dependency ran"));
    }

    /// <summary>Compiles one assembly into <paramref name="directory"/> and returns the file it wrote.</summary>
    private static string Compile(
        string assemblyName,
        string source,
        string directory,
        params string[] references)
    {
        var metadata = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Select(path => MetadataReference.CreateFromFile(path))
            .Cast<MetadataReference>()
            .Concat(references.Select(path => MetadataReference.CreateFromFile(path)));

        var compilation = CSharpCompilation.Create(
            assemblyName,
            [CSharpSyntaxTree.ParseText(source)],
            metadata,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var path = Path.Combine(directory, assemblyName + ".dll");
        var result = compilation.Emit(path);
        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
        return path;
    }
}
