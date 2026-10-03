using System.Security.Cryptography;
using System.Text.Json;
using AsmResolver.DotNet;
using AsmResolver.DotNet.Serialized;
using AsmResolver.DotNet.Signatures;
using Il2CppInterop.Generator;
using Il2CppInterop.Generator.Runners;

namespace Mystia.InteropGen;

public static class Program
{
    private const string ExpectedHash = "91CE5AE3DAD5DA07DFED63BAB4C9E454F67B6E50F9A6E8EC498EF9B0B806A789";

    public static int Main(string[] args)
    {
        // Il2CppInterop's own naming is what the sources reference: compiler generated members such as
        // "<>c__DisplayClass79_0" or "<MainChallengeLoop>d__16" are not valid C# identifiers, so the
        // generator has to sanitise them. Passing the source names through verbatim produces assemblies
        // nothing can reference, so sanitising is the default and --passthrough only exists for comparing.
        var passthroughNames = args.Contains("--passthrough", StringComparer.OrdinalIgnoreCase);
        var positional = args.Where(arg => !arg.StartsWith("--", StringComparison.Ordinal)).ToArray();

        // The managed backup exists in several flavours per machine: an IL2CPP build writes a stripped copy next
        // to the build output, while a Symbols build keeps a fuller one. The flavour decides whether members
        // such as ResourceProviderBase.Release exist in the interop, so the choice must not depend on which
        // directory the recursive search happens to reach first. --managed picks one explicitly.
        var explicitManaged = ArgumentValue(args, "--managed");
        var symbolsOnly = args.Contains("--symbols-backup", StringComparer.OrdinalIgnoreCase);

        if (positional.Length < 2 || string.IsNullOrWhiteSpace(positional[0]) || string.IsNullOrWhiteSpace(positional[1]))
        {
            Console.Error.WriteLine("Usage: Mystia.InteropGen <game-project-dir> <game-install-dir> [output-dir] [unity-libs-dir] [--managed <dir>] [--symbols-backup] [--passthrough]");
            Console.Error.WriteLine("The project directory must contain a Build folder holding a Managed backup, such as");
            Console.Error.WriteLine("Build\\Symbols\\...\\Managed or Build\\<game>_BackUpThisFolder_ButDontShipItWithYourGame\\Managed.");
            Console.Error.WriteLine("The install directory must contain GameAssembly.dll and global-metadata.dat.");
            Console.Error.WriteLine("--managed takes the Managed directory verbatim (a full one is preferred over a stripped copy).");
            Console.Error.WriteLine("--symbols-backup requires the chosen backup to live under a Symbols folder.");
            Console.Error.WriteLine("--passthrough keeps the source names verbatim, which no C# source can reference.");
            return 1;
        }

        var repo = FindRepoRoot();
        var managed = string.IsNullOrWhiteSpace(explicitManaged) ? FindManaged(positional[0], symbolsOnly) : explicitManaged;
        var gameAssembly = Path.Combine(positional[1], "GameAssembly.dll");
        var metadata = FindMetadata(positional[1]);
        var output = positional.ElementAtOrDefault(2) ?? Path.Combine(repo, "artifacts", "interop");

        if (!Directory.Exists(managed))
        {
            Console.Error.WriteLine("Managed backup was not found under " + positional[0]);
            return 1;
        }

        ReportBackup(managed);

        var unityLibs = FindUnityLibs(positional.ElementAtOrDefault(3), managed);

        if (!File.Exists(gameAssembly) || metadata is null)
        {
            Console.Error.WriteLine("GameAssembly.dll or global-metadata.dat was not found under " + positional[1]);
            return 1;
        }

        var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(gameAssembly)));
        var metadataHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(metadata)));
        if (!string.Equals(hash, ExpectedHash, StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine($"Refusing to generate interop. GameAssembly hash {hash} is not the pinned 4.4.0e player.");
            return 1;
        }

        Directory.CreateDirectory(output);
        var options = new GeneratorOptions
        {
            Source = LoadManagedAssemblies(managed),
            OutputDir = output,
            UnityBaseLibsDir = unityLibs,
            GameAssemblyPath = gameAssembly,
            PassthroughNames = passthroughNames,
            Parallel = true,
        };

        Console.WriteLine($"Generating interop for {options.Source.Count} assemblies into {output}");
        Il2CppInteropGenerator.Create(options)
            .AddInteropAssemblyGenerator()
            .Run();

        var manifest = new
        {
            gameAssemblySha256 = hash,
            metadataSha256 = metadataHash,
            gameAssemblyPath = gameAssembly,
            metadataPath = metadata,
            managedDir = managed,
            unityLibsDir = unityLibs,
            passthroughNames = passthroughNames,
            outputDir = output,
        };
        File.WriteAllText(
            Path.Combine(output, "interop-manifest.json"),
            JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("Wrote interop-manifest.json");
        return 0;
    }

    private static List<AssemblyDefinition> LoadManagedAssemblies(string managed)
    {
        var resolver = new ManagedAssemblyResolver(managed);
        var runtime = new RuntimeContext(
            new DotNetRuntimeInfo(DotNetRuntimeInfo.NetStandard, new Version(2, 1)),
            resolver);
        var parameters = new ModuleReaderParameters(runtime);
        var source = new List<AssemblyDefinition>();
        foreach (var file in Directory.EnumerateFiles(managed, "*.dll"))
        {
            AssemblyDefinition assembly;
            try
            {
                assembly = AssemblyDefinition.FromFile(file, parameters);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Skipping {Path.GetFileName(file)}: {ex.Message}");
                continue;
            }

            resolver.Remember(assembly);
            source.Add(assembly);
        }

        RetargetReferences(source);
        StripCustomModifiers(source);
        return source;
    }

    private static void StripCustomModifiers(List<AssemblyDefinition> source)
    {
        foreach (var assembly in source)
        {
            foreach (var type in assembly.ManifestModule!.GetAllTypes())
            {
                foreach (var method in type.Methods)
                {
                    var signature = method.Signature;
                    if (signature == null)
                        continue;
                    signature.ReturnType = StripModifiers(signature.ReturnType);
                    for (var i = 0; i < signature.ParameterTypes.Count; i++)
                        signature.ParameterTypes[i] = StripModifiers(signature.ParameterTypes[i]);
                }

                foreach (var field in type.Fields)
                {
                    if (field.Signature != null)
                        field.Signature.FieldType = StripModifiers(field.Signature.FieldType);
                }

                foreach (var property in type.Properties)
                {
                    var signature = property.Signature;
                    if (signature == null)
                        continue;
                    signature.ReturnType = StripModifiers(signature.ReturnType);
                    for (var i = 0; i < signature.ParameterTypes.Count; i++)
                        signature.ParameterTypes[i] = StripModifiers(signature.ParameterTypes[i]);
                }
            }
        }
    }

    private static TypeSignature StripModifiers(TypeSignature signature)
    {
        switch (signature)
        {
            case CustomModifierTypeSignature modified:
                return StripModifiers(modified.BaseType);
            case GenericInstanceTypeSignature generic:
                for (var i = 0; i < generic.TypeArguments.Count; i++)
                    generic.TypeArguments[i] = StripModifiers(generic.TypeArguments[i]);
                return generic;
            case ByReferenceTypeSignature byReference:
                var strippedReference = StripModifiers(byReference.BaseType);
                return ReferenceEquals(strippedReference, byReference.BaseType)
                    ? byReference
                    : new ByReferenceTypeSignature(strippedReference);
            case PointerTypeSignature pointer:
                var strippedPointer = StripModifiers(pointer.BaseType);
                return ReferenceEquals(strippedPointer, pointer.BaseType)
                    ? pointer
                    : new PointerTypeSignature(strippedPointer);
            case SzArrayTypeSignature array:
                var strippedArray = StripModifiers(array.BaseType);
                return ReferenceEquals(strippedArray, array.BaseType)
                    ? array
                    : new SzArrayTypeSignature(strippedArray);
            case ArrayTypeSignature multi:
                var strippedMulti = StripModifiers(multi.BaseType);
                return ReferenceEquals(strippedMulti, multi.BaseType)
                    ? multi
                    : new ArrayTypeSignature(strippedMulti, multi.Rank);
            default:
                return signature;
        }
    }

    private static void RetargetReferences(List<AssemblyDefinition> source)
    {
        var byName = new Dictionary<string, AssemblyDescriptor>(StringComparer.OrdinalIgnoreCase);
        foreach (var assembly in source)
        {
            var descriptor = (AssemblyDescriptor)assembly;
            var name = descriptor.Name?.ToString();
            if (!string.IsNullOrEmpty(name))
                byName[name] = descriptor;
        }

        foreach (var assembly in source)
        {
            foreach (var reference in assembly.ManifestModule!.AssemblyReferences)
            {
                var name = reference.Name?.ToString();
                if (name == null || !byName.TryGetValue(name, out var target))
                    continue;

                reference.Version = target.Version;
                reference.Culture = target.Culture;
                reference.HasPublicKey = false;
                reference.PublicKeyOrToken = target.GetPublicKeyToken();
            }
        }
    }

    // A build writes its stripped managed copy under the build output and keeps a fuller one under Symbols.
    // Both are called "Managed", so the search prefers the Symbols flavour and only falls back to the others
    // (and says what it picked) instead of taking whatever the enumeration reaches first.
    private static string FindManaged(string projectDir, bool symbolsOnly)
    {
        var build = Path.Combine(projectDir, "Build");
        if (!Directory.Exists(build))
            return "";

        var candidates = Directory.EnumerateDirectories(build, "Managed", SearchOption.AllDirectories)
            .Where(dir => File.Exists(Path.Combine(dir, "Assembly-CSharp.dll")))
            .OrderBy(dir => dir, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var symbols = candidates.FirstOrDefault(dir => dir.Contains("Symbols", StringComparison.OrdinalIgnoreCase));
        if (symbolsOnly)
            return symbols ?? "";

        if (symbols is not null)
            return symbols;

        if (candidates.Length > 1)
        {
            Console.Error.WriteLine($"Several managed backups were found; using '{candidates[0]}'.");
            Console.Error.WriteLine("Pass --managed <dir> to pick one, or --symbols-backup to require a Symbols backup.");
        }

        return candidates.FirstOrDefault() ?? "";
    }

    // The chosen backup decides whether members such as ResourceProviderBase.Release exist in the interop, so it
    // is printed rather than left to be dug out of the manifest. A stripped backup is a common cause of a bridge
    // that will not compile; the way out is --managed <game project>\\Library\\ScriptAssemblies, which holds the
    // unstripped build output. (Neither flavour is authoritative for what the player can resolve at run time:
    // that is the pinned global-metadata.dat, and a member the shipped metadata lacks must never be called.)
    private static void ReportBackup(string managed)
    {
        Console.WriteLine($"Managed backup: {managed}");
        if (!managed.Contains("Symbols", StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine("Note: this backup is not the Symbols flavour. If a bridge member fails to override");
            Console.Error.WriteLine("(ResourceProviderBase.Release is the usual one), pass --managed <game-project>\\Library\\ScriptAssemblies.");
        }
    }

    // The IL2CPP managed backup carries Unity's own assemblies next to the game assemblies,
    // so it is the default source of Unity base libraries. An explicit directory may override it.
    private static string FindUnityLibs(string? explicitDir, string managed)
    {
        if (string.IsNullOrWhiteSpace(explicitDir))
            return managed;

        if (File.Exists(Path.Combine(explicitDir, "UnityEngine.CoreModule.dll")))
            return explicitDir;

        Console.Error.WriteLine(explicitDir + " has no UnityEngine.CoreModule.dll; using the managed backup instead.");
        return managed;
    }

    private static string? FindMetadata(string installDir)
    {
        if (!Directory.Exists(installDir))
            return null;
        return Directory.EnumerateFiles(installDir, "global-metadata.dat", SearchOption.AllDirectories)
            .FirstOrDefault();
    }

    private static string? ArgumentValue(string[] args, string name)
    {
        for (var index = 0; index < args.Length - 1; index++)
        {
            if (string.Equals(args[index], name, StringComparison.OrdinalIgnoreCase))
                return args[index + 1];
        }

        return null;
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "MystiaExtensionFramework.sln"))
                || File.Exists(Path.Combine(directory.FullName, "Directory.Build.props")))
                return directory.FullName;
            directory = directory.Parent;
        }

        return Directory.GetCurrentDirectory();
    }
}
