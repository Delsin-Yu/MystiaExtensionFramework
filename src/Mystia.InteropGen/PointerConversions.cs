using AsmResolver.DotNet;
using AsmResolver.DotNet.Code.Cil;
using AsmResolver.PE.DotNet.Metadata.Tables;
using AsmResolver.DotNet.Signatures;
using AsmResolver.PE.DotNet.Cil;

namespace Mystia.InteropGen;

/// <summary>
/// Rewrites the conversion Il2CppInterop emits to turn an invoked method's <c>IntPtr</c> result back into a
/// pointer parameter.
///
/// A method that takes a <c>byte*&amp;</c> (or any other pointer by reference) is invoked through
/// <c>il2cpp_runtime_invoke</c> and the write-back goes through the parameter's address. The generator builds
/// that store as <c>newobj T*::.ctor(System.IntPtr)</c>, the managed-pointer constructor the CLR kept from
/// .NET Framework's <c>System.Pointer</c>. It does not exist any more: the JIT resolves the token to
/// <c>System.UIntPtr::.ctor(IntPtr)</c>, which was also dropped in .NET Core, and reports
/// <c>MissingMethodException</c> - the whole method can then never be compiled, so every managed caller of it
/// fails instead of running. Nineteen methods of this interop are lost that way, all of them pointer-heavy
/// mscorlib internals (<c>Il2CppSystem.Number</c>, the <c>Text</c> encoders, <c>SafeBuffer</c>, InputSystem's
/// buffers).
///
/// The replacement is the conversion the C# compiler emits for the same cast: <c>IntPtr.ToPointer()</c>, whose
/// <c>void*</c> result is the pointer (every pointer type is the same native integer on the evaluation stack).
/// Only a constructor the generator could have emitted is rewritten - a one-parameter instance <c>.ctor</c> on a
/// pointer type whose parameter is <c>System.IntPtr</c>; anything else is reported.
/// </summary>
internal sealed class PointerConversionPass
{
    private readonly Dictionary<string, int> _kinds = new(StringComparer.Ordinal);
    private readonly List<string> _unrecognised = [];
    private int _rewritten;

    private PointerConversionPass()
    {
    }

    internal static PointerConversionReport Rewrite(IReadOnlyList<AssemblyDefinition> assemblies)
    {
        var pass = new PointerConversionPass();
        foreach (var assembly in assemblies)
            foreach (var type in assembly.ManifestModule!.GetAllTypes())
                foreach (var method in type.Methods)
                    pass.Rewrite(method);

        return new PointerConversionReport(pass._rewritten, pass._kinds, pass._unrecognised);
    }

    private void Rewrite(MethodDefinition method)
    {
        var body = method.CilMethodBody;
        if (body is null)
            return;

        foreach (var instruction in body.Instructions)
        {
            if (instruction.OpCode.Code != CilCode.Newobj || instruction.Operand is not IMethodDefOrRef call)
                continue;
            if (call.DeclaringType?.ToTypeSignature() is not PointerTypeSignature pointer)
                continue;
            if (call.Name?.ToString() != ".ctor")
                continue;
            if (call.Signature is not MethodSignature { ParameterTypes: [var argument] }
                || argument.ElementType != ElementType.I)
            {
                _unrecognised.Add($"{method.FullName}: {call.FullName} is not the IntPtr conversion");
                continue;
            }

            instruction.ReplaceWith(CilOpCodes.Call, ToPointer(method.Module!));
            _rewritten++;
            var kind = pointer.BaseType.FullName ?? "?";
            _kinds[kind] = _kinds.GetValueOrDefault(kind) + 1;
        }
    }

    /// <summary>
    /// <c>System.IntPtr.ToPointer()</c>, the call the C# compiler emits for <c>(T*)intPtr</c>.
    /// </summary>
    private static MemberReference ToPointer(ModuleDefinition module) => new(
        module.CorLibTypeFactory.IntPtr.ToTypeDefOrRef(),
        "ToPointer",
        MethodSignature.CreateInstance(new PointerTypeSignature(module.CorLibTypeFactory.Void)));
}

internal sealed record PointerConversionReport(int Rewritten, IReadOnlyDictionary<string, int> Kinds, IReadOnlyList<string> Unrecognised)
{
    internal string Summary()
    {
        var kinds = string.Join(", ", Kinds.Select(kind => $"{kind.Value} {kind.Key}*"));
        var summary = $"{Rewritten} pointer conversions rewritten";
        if (kinds.Length > 0)
            summary += $" ({kinds})";
        return summary + $", {Unrecognised.Count} left alone";
    }
}
