using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Mystia.SourceGenerators;
using Xunit;

namespace Mystia.Tests;

public sealed class ModEntranceGeneratorTests
{
    [Fact]
    public void EmitsOneInstanceRegisteredForEveryAutoWireInterface()
    {
        var source = """
            using Mystia;
            using Mystia.Scenes;
            namespace SampleMod
            {
                public sealed class SceneLog : ISceneListener, IPostInitialize
                {
                    public void OnSceneStart(SceneId scene) { }
                    public void PostInitialize(IModContext context) { }
                }
            }
            """;

        var generated = Generate(source, out var diagnostics);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Contains("new global::SampleMod.SceneLog()", generated);
        Assert.Contains("registrar.Add<global::Mystia.Scenes.ISceneListener>(instance0);", generated);
        Assert.Contains("registrar.Add<global::Mystia.IPostInitialize>(instance0);", generated);
        Assert.Contains("ModEntranceAttribute(typeof(global::Mystia.Generated.ModEntrance))", generated);
    }

    [Fact]
    public void ReportsMystia001WhenTheParameterlessConstructorIsMissing()
    {
        var source = """
            using Mystia;
            using Mystia.Scenes;
            namespace SampleMod
            {
                public sealed class NeedsArgs : ISceneListener
                {
                    public NeedsArgs(int value) { }
                    public void OnSceneStart(SceneId scene) { }
                }
            }
            """;

        var generated = Generate(source, out var diagnostics);

        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == ModEntranceGenerator.DiagnosticId);
        Assert.DoesNotContain("NeedsArgs", generated);
        Assert.Contains("public static void Register", generated);
    }

    [Fact]
    public void IgnoresAbstractAndGenericImplementers()
    {
        var source = """
            using Mystia;
            using Mystia.Scenes;
            namespace SampleMod
            {
                public abstract class BaseLog : ISceneListener
                {
                    public void OnSceneStart(SceneId scene) { }
                }

                public sealed class GenericLog<T> : ISceneListener
                {
                    public void OnSceneStart(SceneId scene) { }
                }
            }
            """;

        var generated = Generate(source, out var diagnostics);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == ModEntranceGenerator.DiagnosticId);
        Assert.DoesNotContain("BaseLog", generated);
        Assert.DoesNotContain("GenericLog", generated);
    }

    [Fact]
    public void EmitsKeyedRegistrationWhenTheInterfaceDeclaresAStringKey()
    {
        var source = """
            using Mystia;
            namespace SampleMod
            {
                public sealed class Tagged : IKeyedListener
                {
                    public string Key => "alpha";
                }
            }
            """;
        var extraSdk = """
            namespace Mystia
            {
                [AutoWire]
                public interface IKeyedListener
                {
                    string Key { get; }
                }
            }
            """;

        var generated = Generate(source, out var diagnostics, extraSdk);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Contains("registrar.Add<global::Mystia.IKeyedListener>(instance0.Key, instance0);", generated);
    }

    private static string Generate(string source, out IReadOnlyList<Diagnostic> diagnostics, string? extraSource = null)
    {
        var trees = new List<SyntaxTree> { CSharpSyntaxTree.ParseText(source) };
        if (extraSource is not null)
            trees.Add(CSharpSyntaxTree.ParseText(extraSource));

        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Select(path => MetadataReference.CreateFromFile(path))
            .Cast<MetadataReference>()
            .ToList();
        references.Add(MetadataReference.CreateFromFile(typeof(AutoWireAttribute).Assembly.Location));

        var compilation = CSharpCompilation.Create(
            "ModUnderTest",
            trees,
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var driver = CSharpGeneratorDriver.Create(new ModEntranceGenerator());
        driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var generatorDiagnostics);
        diagnostics = generatorDiagnostics;
        return output.SyntaxTrees.Single(tree => tree.FilePath.EndsWith("ModEntrance.g.cs", StringComparison.Ordinal)).ToString();
    }
}
