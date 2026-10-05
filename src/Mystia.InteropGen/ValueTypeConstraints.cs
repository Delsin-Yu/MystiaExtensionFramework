using AsmResolver.DotNet;
using AsmResolver.DotNet.Signatures;

namespace Mystia.InteropGen;

/// <summary>
/// Rewrites a generic parameter's <c>System.ValueType</c> constraint from the generated mirror back to the real
/// value type.
///
/// A constraint is not copied through the same translator as a field or a parameter type.
/// Pass13FillGenericConstraints adds a constraint whose type is anything but the plain <c>System.ValueType</c> -
/// and a source constraint of <c>where T : unmanaged</c> is <c>System.ValueType</c> carrying a
/// <c>modreq(UnmanagedType)</c> modifier, which is not that plain type - and it rewrites the reference like any
/// other, so the constraint lands on the generated <c>Il2CppSystem.ValueType</c>. That type is the mirror of the
/// il2cpp class of the same name and is itself a class (it derives from <c>Il2CppSystem.Object</c>), so no real
/// value type satisfies it: every instantiation of the generic loses its type
/// (<c>TypeLoadException 0x80131522</c>). <c>Unity.Collections.UnmanagedArray&lt;T&gt;</c>,
/// <c>FixedList4096Bytes&lt;T&gt;</c> and <c>AllocatorManager.Array32768&lt;T&gt;</c> are the ones this interop
/// trips over; the arguments they are used with are ordinary value types (<c>MemoryBlock</c>, <c>TableEntry</c>,
/// <c>int</c>).
///
/// The real <c>System.ValueType</c> is the constraint the input asked for, and the one the C# compiler of a mod
/// sees. Only a constraint that names the mirror exactly is rewritten; nothing else is touched.
/// </summary>
internal sealed class ValueTypeConstraintPass
{
    private const string Mirror = "Il2CppSystem.ValueType";
    private const string Real = "System.ValueType";

    private readonly List<string> _unrecognised = [];
    private int _rewritten;

    private ValueTypeConstraintPass()
    {
    }

    internal static ValueTypeConstraintReport Rewrite(IReadOnlyList<AssemblyDefinition> assemblies)
    {
        var pass = new ValueTypeConstraintPass();
        foreach (var assembly in assemblies)
        {
            var module = assembly.ManifestModule!;
            foreach (var type in module.GetAllTypes())
            {
                pass.Rewrite(module, type.GenericParameters);
                foreach (var method in type.Methods)
                    pass.Rewrite(module, method.GenericParameters);
            }
        }

        return new ValueTypeConstraintReport(pass._rewritten, pass._unrecognised);
    }

    private void Rewrite(ModuleDefinition module, IList<GenericParameter> parameters)
    {
        var valueType = module.DefaultImporter.ImportType(
            module.CorLibTypeFactory.CorLibScope.CreateTypeReference("System", "ValueType"));

        foreach (var parameter in parameters)
        foreach (var constraint in parameter.Constraints)
        {
            if (constraint.Constraint?.FullName == Mirror)
            {
                constraint.Constraint = valueType;
                _rewritten++;
            }
            else if (constraint.Constraint?.FullName != Real && constraint.Constraint?.FullName?.Contains("ValueType") == true)
            {
                _unrecognised.Add($"{parameter.Name} is constrained to {constraint.Constraint.FullName}");
            }
        }
    }
}

internal sealed record ValueTypeConstraintReport(int Rewritten, IReadOnlyList<string> Unrecognised)
{
    internal string Summary() => $"{Rewritten} value type constraints moved off the mirror, {Unrecognised.Count} other";
}
