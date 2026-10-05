using AsmResolver.DotNet;
using AsmResolver.PE.DotNet.Metadata.Tables;

namespace Mystia.InteropGen;

/// <summary>
/// Clears the <c>ComImport</c> flag the generated mirror of a COM type inherits from its input.
///
/// Il2CppInterop turns every input type into a class deriving from <c>Il2CppObjectBase</c> and drops the
/// interface flag as it goes (Pass10CreateTypedefs.AdjustAttributes clears <c>Interface</c>), but it copies the
/// rest of the input's type attributes, and a COM type carries <c>ComImport</c> (<c>System.Runtime.InteropServices.ComTypes</c>
/// is full of them). The loader refuses that combination: a type marked as imported may not extend anything but
/// <c>System.Object</c>, and the mirror extends <c>Il2CppObjectBase</c>, so the type never loads -
/// "Type '...IAdviseSink' ... cannot extend from any other type". Five types of this interop are lost that way
/// (<c>IMoniker</c>, <c>IAdviseSink</c>, <c>IDataObject</c>, <c>IEnumFORMATETC</c>, <c>IEnumSTATDATA</c>), and
/// every method that touches one of them fails to compile with it.
///
/// The flag describes the input's native type library, not the mirror: the mirror is an il2cpp object like any
/// other, and nothing in the interop or the runtime reads the flag. The other attributes the type carries
/// (<c>Guid</c>, <c>InterfaceType</c>) are left as they are.
/// </summary>
internal sealed class ComImportTypePass
{
    private readonly List<string> _cleared = [];

    private ComImportTypePass()
    {
    }

    internal static ComImportTypeReport Rewrite(IReadOnlyList<AssemblyDefinition> assemblies)
    {
        var pass = new ComImportTypePass();
        foreach (var assembly in assemblies)
            foreach (var type in assembly.ManifestModule!.GetAllTypes())
            {
                if ((type.Attributes & TypeAttributes.Import) == 0)
                    continue;
                type.Attributes &= ~TypeAttributes.Import;
                pass._cleared.Add($"{type.FullName} (base {type.BaseType?.FullName})");
            }

        return new ComImportTypeReport(pass._cleared);
    }
}

internal sealed record ComImportTypeReport(IReadOnlyList<string> Cleared)
{
    internal string Summary() => $"{Cleared.Count} ComImport flags cleared";
}
