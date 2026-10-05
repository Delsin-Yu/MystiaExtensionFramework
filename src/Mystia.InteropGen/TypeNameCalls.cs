using AsmResolver.DotNet;
using AsmResolver.DotNet.Code.Cil;
using AsmResolver.DotNet.Signatures;
using AsmResolver.PE.DotNet.Cil;

namespace Mystia.InteropGen;

/// <summary>
/// Rewrites the type name calls Il2CppInterop emits for a type the CLR refuses as a generic argument.
///
/// The generator renders a parameter type through <c>IL2CPP.RenderTypeName&lt;T&gt;(bool)</c> and only
/// unwraps a by-reference type, so a pointer parameter becomes <c>RenderTypeName&lt;System.Byte*&gt;</c>
/// (Pass20GenerateStaticConstructors.EmitLoadTypeNameString). A pointer is not a valid type argument: the
/// CLR refuses to instantiate the method, and the failure lands on the whole method around the call - its
/// static constructor can never be JIT-compiled, and every attempt is reported as
/// <c>BadImageFormatException</c> (0x8007000B, "the format of the program is incorrect"). The type then
/// fails at its first use: everything that touches one of its static members - string marshalling, the
/// pointer caches of <c>Il2CppSystem.String</c>, any array of generated objects - throws instead of running,
/// and a native caller sees only the interop's default value.
///
/// The same holds for a by-reference-like type, and one reaches these assemblies: the generator hands
/// <c>System.TypedReference</c> to the runtime instead of mirroring it
/// (AssemblyRewriteContext.RewriteTypeRef, the one type on that list), and a byref-like type is not a valid
/// type argument either. <c>Il2CppSystem.TypedReference</c>, <c>Reflection.FieldInfo</c> and
/// <c>Reflection.RuntimeFieldInfo</c> lose their constructors to it.
///
/// The rewrite targets the overload the same runtime type already offers for a <c>System.Type</c>:
/// <c>RenderTypeName(typeof(T*), marker)</c> renders the very same string - the generic overload is one
/// forward to it - but carries no generic instantiation and compiles. A call is only rewritten when the
/// argument in front of it is the constant marker the generator emits; anything else is left alone and
/// reported, because the string that would come out of a guess is the name the engine is looked up by.
/// </summary>
internal sealed class TypeNameCallPass
{
    private const string RenderTypeName = "RenderTypeName";
    private const string RuntimeType = "Il2CppInterop.Runtime.IL2CPP";

    private readonly List<string> _unrecognised = [];
    private readonly Dictionary<string, int> _kinds = new(StringComparer.Ordinal);
    private int _rewritten;

    private TypeNameCallPass()
    {
    }

    internal static TypeNameCallReport Rewrite(IReadOnlyList<AssemblyDefinition> assemblies)
    {
        var pass = new TypeNameCallPass();
        foreach (var assembly in assemblies)
            foreach (var type in assembly.ManifestModule!.GetAllTypes())
                foreach (var method in type.Methods)
                    pass.Rewrite(method);

        return new TypeNameCallReport(pass._rewritten, pass._kinds, pass._unrecognised);
    }

    private void Rewrite(MethodDefinition method)
    {
        var body = method.CilMethodBody;
        if (body is null)
            return;

        var instructions = body.Instructions;
        // Downwards: a rewrite inserts an instruction, so only the indices below it keep their meaning.
        for (var index = instructions.Count - 1; index > 0; index--)
        {
            if (instructions[index].Operand is not MethodSpecification call)
                continue;
            var rendered = call.Method;
            if (rendered?.Name?.ToString() != RenderTypeName || rendered.DeclaringType?.FullName != RuntimeType)
                continue;
            if (call.Signature?.TypeArguments is not { Count: 1 } arguments)
                continue;

            var kind = Describe(arguments[0]);
            if (kind is null)
                continue;

            var marker = instructions[index - 1];
            if (!marker.IsLdcI4())
            {
                _unrecognised.Add($"{method.FullName}: {RenderTypeName}<{arguments[0].FullName}> follows {marker.OpCode}");
                continue;
            }

            // The overload that takes the type itself. Its declaring type and the rendered type are the ones
            // the call already uses, so the only thing that changes is how the type reaches the call: the
            // pointer is handed over as a type token instead of as a generic argument. A type token is a
            // RuntimeTypeHandle, so it goes through Type.GetTypeFromHandle, which is what typeof() compiles to.
            var module = method.Module!;
            var reference = new MemberReference(
                rendered.DeclaringType!,
                RenderTypeName,
                MethodSignature.CreateStatic(
                    module.CorLibTypeFactory.String,
                    [TypeOfType(module), module.CorLibTypeFactory.Boolean]));

            var addRefMarker = marker.GetLdcI4Constant();
            marker.ReplaceWith(CilOpCodes.Ldtoken, module.DefaultImporter.ImportTypeSignature(arguments[0]).ToTypeDefOrRef());
            instructions.Insert(index, new CilInstruction(CilOpCodes.Call, TypeFromHandle(module)));
            instructions.Insert(index + 1, new CilInstruction(CilOpCodes.Ldc_I4, addRefMarker));
            instructions[index + 2].Operand = reference;

            _rewritten++;
            _kinds[kind] = _kinds.GetValueOrDefault(kind) + 1;
        }
    }

    private static TypeSignature TypeOfType(ModuleDefinition module) =>
        module.DefaultImporter.ImportTypeSignature(
            module.CorLibTypeFactory.CorLibScope.CreateTypeReference("System", "Type").ToTypeSignature());

    /// <summary>
    /// <c>System.Type.GetTypeFromHandle(System.RuntimeTypeHandle)</c>, the call a type token needs to become
    /// a <c>System.Type</c>.
    /// </summary>
    private static MemberReference TypeFromHandle(ModuleDefinition module)
    {
        var importer = module.DefaultImporter;
        var handle = importer.ImportType(module.CorLibTypeFactory.CorLibScope.CreateTypeReference("System", "RuntimeTypeHandle"));
        return new MemberReference(
            importer.ImportType(module.CorLibTypeFactory.CorLibScope.CreateTypeReference("System", "Type")),
            "GetTypeFromHandle",
            MethodSignature.CreateStatic(TypeOfType(module), [new TypeDefOrRefSignature(handle, isValueType: true)]));
    }

    // A type argument the runtime will not instantiate. A by-reference type is reported rather than rewritten:
    // the generator unwraps those itself, and no reading of a pointer to a by-reference is worth guessing at.
    private static string? Describe(TypeSignature type) => type switch
    {
        PointerTypeSignature => "pointer",
        FunctionPointerTypeSignature => "function pointer",
        _ when IsByReferenceLike(type) => "byref-like",
        _ => null,
    };

    /// <summary>
    /// Whether the CLR refuses this type as a generic argument because it can only live on the stack: a
    /// <c>ref struct</c>, either one whose definition carries <c>IsByRefLikeAttribute</c> or one of the three
    /// the runtime has always treated that way.
    /// </summary>
    private static bool IsByReferenceLike(TypeSignature type)
    {
        var name = type.FullName;
        if (name is "System.TypedReference" or "System.ArgIterator" or "System.RuntimeArgumentHandle")
            return true;

        var definition = (type as TypeDefOrRefSignature)?.Type.Resolve();
        return definition is not null
            && definition.CustomAttributes.Any(attribute =>
                attribute.Constructor?.DeclaringType?.Name?.ToString() == "IsByRefLikeAttribute");
    }
}

internal sealed record TypeNameCallReport(int Rewritten, IReadOnlyDictionary<string, int> Kinds, IReadOnlyList<string> Unrecognised)
{
    internal string Summary()
    {
        var kinds = string.Join(", ", Kinds.Select(kind => $"{kind.Value} {kind.Key}"));
        var summary = $"{Rewritten} type name calls rewritten";
        if (kinds.Length > 0)
            summary += $" ({kinds})";
        return summary + $", {Unrecognised.Count} left alone";
    }
}
