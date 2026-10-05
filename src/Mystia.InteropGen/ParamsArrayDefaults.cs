using AsmResolver.DotNet;
using AsmResolver.DotNet.Code.Cil;
using AsmResolver.DotNet.Collections;
using AsmResolver.DotNet.Signatures;
using AsmResolver.PE.DotNet.Cil;
using AsmResolver.PE.DotNet.Metadata.Tables;

namespace Mystia.InteropGen;

/// <summary>
/// Rewrites the array Il2CppInterop builds when a <c>params</c> parameter arrives as null, so that it is the
/// array the parameter was declared with.
///
/// The parameter itself is converted by <c>RewriteTypeRef</c>, which picks the wrapper from the *generated*
/// element type: <c>Il2CppStructArray&lt;T&gt;</c> (whose <c>T</c> must be unmanaged) for a value type and
/// <c>Il2CppReferenceArray&lt;T&gt;</c> for everything else, and <c>Il2CppArrayBase&lt;T&gt;</c> - the abstract
/// base, because neither wrapper can name an unknown element type - when the element is a generic parameter.
/// The prologue (Pass50GenerateMethods) decides on its own from the *input* element type and always instantiates
/// a concrete wrapper. The two disagree wherever the generator turned a struct into a class: a struct that holds
/// a reference, or any generic struct, is generated as a class deriving from the <c>Il2CppSystem.ValueType</c>
/// mirror, so the parameter is an <c>Il2CppReferenceArray</c> while the prologue builds an
/// <c>Il2CppStructArray</c> for it - an instantiation the CLR refuses (T must be unmanaged), reported as
/// <c>TypeLoadException</c>, and the method cannot be compiled at all. Where the parameter is the abstract
/// <c>Il2CppArrayBase&lt;T&gt;</c> the prologue names <c>Il2CppReferenceArray&lt;T&gt;</c> with the generic
/// parameter as its argument, which the CLR refuses as well (T is not known to be an <c>Il2CppObjectBase</c>).
///
/// A prologue is rewritten to the constructor of the parameter's own type, which is what the signature already
/// promises and what the IL that follows expects (the store into the parameter, and every read after it). Where
/// the parameter's type is the abstract base there is no wrapper to instantiate at all: the prologue is removed,
/// so a managed caller that passes null leaves the parameter null instead of failing to compile the method.
/// Only the sequence Pass50 emits is touched - a branch over an <c>(long)</c> constructor store - and only when
/// the store goes to the parameter whose type is being matched; anything else is reported.
/// </summary>
internal sealed class ParamsArrayDefaultPass
{
    private const string Namespace = "Il2CppInterop.Runtime.InteropTypes.Arrays.";
    private const string StructArray = Namespace + "Il2CppStructArray`1";
    private const string ReferenceArray = Namespace + "Il2CppReferenceArray`1";
    private const string AbstractArray = Namespace + "Il2CppArrayBase`1";
    private const string StringArray = Namespace + "Il2CppStringArray";

    private readonly List<string> _unrecognised = [];
    private readonly List<string> _dropped = [];
    private readonly Dictionary<string, int> _kinds = new(StringComparer.Ordinal);
    private int _rewritten;

    private ParamsArrayDefaultPass()
    {
    }

    internal static ParamsArrayDefaultReport Rewrite(IReadOnlyList<AssemblyDefinition> assemblies)
    {
        var pass = new ParamsArrayDefaultPass();
        foreach (var assembly in assemblies)
            foreach (var type in assembly.ManifestModule!.GetAllTypes())
                foreach (var method in type.Methods)
                    pass.Rewrite(method);

        return new ParamsArrayDefaultReport(pass._rewritten, pass._kinds, pass._dropped, pass._unrecognised);
    }

    private void Rewrite(MethodDefinition method)
    {
        var body = method.CilMethodBody;
        if (body is null)
            return;

        var instructions = body.Instructions;
        for (var index = 1; index < instructions.Count; index++)
        {
            if (instructions[index].OpCode.Code is not (CilCode.Starg or CilCode.Starg_S))
                continue;
            if (instructions[index - 1].OpCode.Code != CilCode.Newobj
                || instructions[index - 1].Operand is not IMethodDefOrRef call)
            {
                continue;
            }

            var built = call.DeclaringType?.ToTypeSignature() as GenericInstanceTypeSignature;
            if (built is null || call.Name?.ToString() != ".ctor")
                continue;
            if (built.GenericType.FullName is not (StructArray or ReferenceArray))
                continue;
            if (call.Signature is not MethodSignature { ParameterTypes: [var length] } || length.ElementType != ElementType.I8)
                continue;

            var parameter = instructions[index].GetParameter(method.Parameters);
            if (parameter is null)
                continue;

            var declared = parameter.ParameterType;
            var stored = declared as GenericInstanceTypeSignature;
            var name = stored?.GenericType.FullName;
            if (name == AbstractArray)
            {
                Drop(method, instructions, index, parameter);
                continue;
            }

            if (name is not (StructArray or ReferenceArray))
            {
                _unrecognised.Add($"{method.FullName}: '{parameter.Name}' is a {declared.FullName}, the prologue builds {built.FullName}");
                continue;
            }

            if (stored!.TypeArguments[0].FullName != built.TypeArguments[0].FullName)
            {
                _unrecognised.Add($"{method.FullName}: '{parameter.Name}' holds {stored.TypeArguments[0].FullName}, the prologue builds {built.TypeArguments[0].FullName}");
                continue;
            }

            if (name == built.GenericType.FullName)
                continue;

            // The constructor of the parameter's own type. Its signature takes no type argument, so it is the
            // one the prologue already used; only the type it belongs to changes.
            instructions[index - 1].Operand = new MemberReference(
                method.Module!.DefaultImporter.ImportTypeSignature(declared).ToTypeDefOrRef(),
                ".ctor",
                (MethodSignature)call.Signature!);
            _rewritten++;
            var kind = $"{built.GenericType.Name} -> {stored.GenericType.Name}";
            _kinds[kind] = _kinds.GetValueOrDefault(kind) + 1;
        }
    }

    /// <summary>
    /// Removes <c>ldarg p; brtrue end; ldc.i4.0; conv.i8; newobj ctor; starg p</c> - the prologue of a
    /// <c>params</c> parameter whose declared type is the abstract array base, which has no constructor.
    /// </summary>
    private void Drop(MethodDefinition method, CilInstructionCollection instructions, int index, Parameter parameter)
    {
        if (index < 5)
        {
            _unrecognised.Add($"{method.FullName}: '{parameter.Name}' prologue does not start with a branch");
            return;
        }

        var load = instructions[index - 5];
        var branch = instructions[index - 4];
        var zero = instructions[index - 3];
        var convert = instructions[index - 2];
        if (load.OpCode.Code is not (CilCode.Ldarg or CilCode.Ldarg_0 or CilCode.Ldarg_1 or CilCode.Ldarg_2 or CilCode.Ldarg_3 or CilCode.Ldarg_S)
            || load.GetParameter(method.Parameters) != parameter
            || branch.OpCode.Code is not (CilCode.Brtrue or CilCode.Brtrue_S)
            || zero.OpCode.Code != CilCode.Ldc_I4_0
            || convert.OpCode.Code != CilCode.Conv_I8)
        {
            _unrecognised.Add($"{method.FullName}: '{parameter.Name}' prologue has an unexpected shape ({load.OpCode.Code} {branch.OpCode.Code} {zero.OpCode.Code} {convert.OpCode.Code})");
            return;
        }

        for (var at = index - 5; at <= index; at++)
            instructions[at].ReplaceWithNop();
        _dropped.Add($"{method.FullName}: '{parameter.Name}' ({parameter.ParameterType.FullName})");
    }
}

internal sealed record ParamsArrayDefaultReport(
    int Rewritten,
    IReadOnlyDictionary<string, int> Kinds,
    IReadOnlyList<string> Dropped,
    IReadOnlyList<string> Unrecognised)
{
    internal string Summary()
    {
        var kinds = string.Join(", ", Kinds.Select(kind => $"{kind.Value} {kind.Key}"));
        var summary = $"{Rewritten} params array prologues matched their parameter";
        if (kinds.Length > 0)
            summary += $" ({kinds})";
        return $"{summary}, {Dropped.Count} removed, {Unrecognised.Count} left alone";
    }
}
