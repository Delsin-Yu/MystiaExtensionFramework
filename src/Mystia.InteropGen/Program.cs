using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text.Json;
using AsmResolver.DotNet;
using AsmResolver.DotNet.Serialized;
using AssemblyDefinition = AsmResolver.DotNet.AssemblyDefinition;
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

        // An option's value is not a positional argument. Reading them apart matters: `--managed <dir>` used to
        // leave <dir> in the positional list, where it took the output directory's place and the generator wrote
        // the interop straight over the very assemblies it was reading.
        var positional = new List<string>();
        var explicitManaged = "";
        var explicitOutput = "";
        var repairLayouts = "";
        var repairTypeNames = "";
        var symbolsOnly = false;
        var allowStripped = false;
        for (var index = 0; index < args.Length; index++)
        {
            var arg = args[index];
            switch (arg.ToLowerInvariant())
            {
                case "--managed" when index + 1 < args.Length:
                    explicitManaged = args[++index];
                    continue;
                case "--output" when index + 1 < args.Length:
                    explicitOutput = args[++index];
                    continue;
                case "--repair-layouts" when index + 1 < args.Length:
                    repairLayouts = args[++index];
                    continue;
                case "--repair-type-names" when index + 1 < args.Length:
                    repairTypeNames = args[++index];
                    continue;
                case "--symbols-backup":
                    symbolsOnly = true;
                    continue;
                case "--allow-stripped-backup":
                    allowStripped = true;
                    continue;
                case "--passthrough":
                    continue;
            }

            if (!arg.StartsWith("--", StringComparison.Ordinal))
                positional.Add(arg);
        }

        if (!string.IsNullOrWhiteSpace(repairLayouts))
        {
            if (!Directory.Exists(repairLayouts))
            {
                Console.Error.WriteLine("Interop directory was not found: " + repairLayouts);
                return 1;
            }

            return RepairLayouts(repairLayouts);
        }

        if (!string.IsNullOrWhiteSpace(repairTypeNames))
        {
            if (!Directory.Exists(repairTypeNames))
            {
                Console.Error.WriteLine("Interop directory was not found: " + repairTypeNames);
                return 1;
            }

            return RepairTypeNames(repairTypeNames);
        }

        if (positional.Count < 2 || string.IsNullOrWhiteSpace(positional[0]) || string.IsNullOrWhiteSpace(positional[1]))
        {
            Console.Error.WriteLine("Usage: Mystia.InteropGen <game-project-dir> <game-install-dir> [output-dir] [unity-libs-dir] [--managed <dir>] [--output <dir>] [--symbols-backup] [--allow-stripped-backup] [--passthrough]");
            Console.Error.WriteLine("       Mystia.InteropGen --repair-layouts <interop-dir>");
            Console.Error.WriteLine("       Mystia.InteropGen --repair-type-names <interop-dir>");
            Console.Error.WriteLine("The project directory must contain a Build folder holding a Managed backup, such as");
            Console.Error.WriteLine("Build\\Symbols\\...\\Managed or Build\\<game>_BackUpThisFolder_ButDontShipItWithYourGame\\Managed.");
            Console.Error.WriteLine("The install directory must contain GameAssembly.dll and global-metadata.dat.");
            Console.Error.WriteLine("--managed takes the Managed directory verbatim (a full one is preferred over a stripped copy).");
            Console.Error.WriteLine("--output sets the interop directory (defaults to the repository's artifacts/interop).");
            Console.Error.WriteLine("--symbols-backup requires the chosen backup to live under a Symbols folder.");
            Console.Error.WriteLine("--allow-stripped-backup accepts a backup with no Symbols sibling, which may leave members out.");
            Console.Error.WriteLine("--passthrough keeps the source names verbatim, which no C# source can reference.");
            Console.Error.WriteLine("--repair-layouts lays out the value types of interop that was generated before this tool did");
            Console.Error.WriteLine("it, in place, and touches nothing else.");
            Console.Error.WriteLine("--repair-type-names rewrites the type name calls a pointer parameter makes uncompileable, in");
            Console.Error.WriteLine("place, and touches nothing else.");
            return 1;
        }

        var repo = FindRepoRoot();
        var managed = string.IsNullOrWhiteSpace(explicitManaged) ? FindManaged(positional[0], symbolsOnly) : explicitManaged;
        var gameAssembly = Path.Combine(positional[1], "GameAssembly.dll");
        var metadata = FindMetadata(positional[1]);
        var output = !string.IsNullOrWhiteSpace(explicitOutput)
            ? explicitOutput
            : positional.ElementAtOrDefault(2) ?? Path.Combine(repo, "artifacts", "interop");

        if (!Directory.Exists(managed))
        {
            Console.Error.WriteLine("Managed backup was not found under " + positional[0]);
            return 1;
        }

        // Writing over the source is the one mistake this tool must not make: it replaces the game's own managed
        // assemblies with generated interop. A directory that holds Assembly-CSharp.dll but no interop manifest
        // is a source, not an output.
        var fullOutput = Path.GetFullPath(output);
        if (string.Equals(fullOutput, Path.GetFullPath(managed), StringComparison.OrdinalIgnoreCase)
            || (File.Exists(Path.Combine(fullOutput, "Assembly-CSharp.dll"))
                && !File.Exists(Path.Combine(fullOutput, "interop-manifest.json"))))
        {
            Console.Error.WriteLine($"Refusing to write generated interop into '{fullOutput}': it holds assembly sources, not interop. Pass --output <dir>.");
            return 1;
        }

        if (!ReportBackup(managed, allowStripped))
            return 1;

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

        // Il2CppInterop only copies field offsets out of the input, so the generated value types have to be
        // laid out before anything runs against them. See FieldLayoutPass.
        // It also renders a pointer parameter's type name through a generic method that cannot be
        // instantiated, which makes the constructor that holds it uncompileable. See TypeNameCallPass.
        var assemblies = ReadInterop(output, out var paths);
        var report = FieldLayoutPass.Materialize(assemblies);
        var typeNames = TypeNameCallPass.Rewrite(assemblies);
        WriteInterop(assemblies, paths);

        Console.WriteLine($"Layouts: {report.Summary()}");
        foreach (var name in report.Unresolved.Take(20))
            Console.WriteLine($"  no size for a field of {name}");
        foreach (var name in report.Flat.Take(20))
            Console.WriteLine($"  still at offset 0: {name}");
        Console.WriteLine($"Type names: {typeNames.Summary()}");
        foreach (var name in typeNames.Unrecognised.Take(20))
            Console.WriteLine($"  left alone: {name}");

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
            valueTypesLaidOut = report.Materialized,
            valueTypesWithoutASize = report.Unresolved,
            valueTypesWithoutAnOffset = report.Flat,
            typeNameCallsRewritten = typeNames.Rewritten,
            typeNameCallsLeftAlone = typeNames.Unrecognised,
        };
        File.WriteAllText(
            Path.Combine(output, "interop-manifest.json"),
            JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("Wrote interop-manifest.json");
        return 0;
    }

    /// <summary>
    /// Lays out the value types of every assembly in an interop directory and writes them back, in place.
    /// </summary>
    private static int RepairLayouts(string directory)
    {
        var assemblies = ReadInterop(directory, out var paths);
        var report = FieldLayoutPass.Materialize(assemblies);
        WriteInterop(assemblies, paths);
        Console.WriteLine($"Layouts: {report.Summary()}");
        foreach (var name in report.Unresolved.Take(20))
            Console.WriteLine($"  no size for a field of {name}");
        foreach (var name in report.Flat.Take(20))
            Console.WriteLine($"  still at offset 0: {name}");
        return 0;
    }

    /// <summary>
    /// Rewrites the type name calls of every assembly in an interop directory, in place.
    /// </summary>
    private static int RepairTypeNames(string directory)
    {
        var assemblies = ReadInterop(directory, out var paths);
        var report = TypeNameCallPass.Rewrite(assemblies);
        WriteInterop(assemblies, paths);
        Console.WriteLine($"Type names: {report.Summary()}");
        foreach (var name in report.Unrecognised.Take(20))
            Console.WriteLine($"  left alone: {name}");
        return 0;
    }

    private static List<AssemblyDefinition> ReadInterop(string directory, out List<string> paths)
    {
        var assemblies = new List<AssemblyDefinition>();
        paths = [];
        foreach (var file in Directory.EnumerateFiles(directory, "*.dll"))
        {
            paths.Add(file);
            assemblies.Add(AssemblyDefinition.FromBytes(File.ReadAllBytes(file)));
        }

        return assemblies;
    }

    private static void WriteInterop(List<AssemblyDefinition> assemblies, List<string> paths)
    {
        for (var index = 0; index < assemblies.Count; index++)
        {
            var temporary = paths[index] + ".interop";
            assemblies[index].Write(temporary);
            File.Move(temporary, paths[index], true);
        }
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

    // The chosen backup decides whether members such as ResourceProviderBase.Release exist in the interop, so the
    // question is answered by reading the backup's own metadata table (a `strings` sweep over a managed assembly
    // gives false negatives). A backup that lacks the member needs --allow-stripped-backup: the interop built
    // from it will not carry it either, and the bridge overrides it.
    private static bool ReportBackup(string managed, bool allowStripped)
    {
        Console.WriteLine($"Managed backup: {managed}");
        var dependencies = Path.Combine(managed, "Unity.ResourceManager.dll");
        if (!File.Exists(dependencies))
        {
            Console.Error.WriteLine("The backup has no Unity.ResourceManager.dll, so the Addressables provider members cannot be checked.");
            return allowStripped;
        }

        if (DeclaresMember(dependencies, "ResourceProviderBase", "Release"))
            return true;

        Console.Error.WriteLine("This backup has no ResourceProviderBase.Release, so the interop built from it will not either and");
        Console.Error.WriteLine("Mystia.Modding.Bridge/Game/AssetProviders.cs will fail to compile (CS0115).");
        Console.Error.WriteLine("Library/ScriptAssemblies is NOT a usable source: the unstripped project assemblies do not match the stripped");
        Console.Error.WriteLine("engine modules and the generator throws NullReferenceException (reproduced on both machines). Use another build's");
        Console.Error.WriteLine("fuller Managed backup, or take the generated interop from a machine whose backup has the member.");
        return allowStripped;
    }

    // Reads the assembly's metadata table, not its string heap: whether a type declares a member is a table
    // question, and a name can be absent from the heap while the member exists.
    private static bool DeclaresMember(string assemblyPath, string typeName, string memberName)
    {
        using var stream = File.OpenRead(assemblyPath);
        using var reader = new PEReader(stream);
        if (!reader.HasMetadata)
            return false;

        var metadata = reader.GetMetadataReader();
        foreach (var handle in metadata.TypeDefinitions)
        {
            var type = metadata.GetTypeDefinition(handle);
            if (!metadata.GetString(type.Name).Contains(typeName, StringComparison.Ordinal))
                continue;

            foreach (var methodHandle in type.GetMethods())
            {
                if (metadata.GetString(metadata.GetMethodDefinition(methodHandle).Name).Contains(memberName, StringComparison.Ordinal))
                    return true;
            }
        }

        return false;
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
