using AsmResolver.DotNet;
using AsmResolver.DotNet.Signatures;
using AsmResolver.PE.DotNet.Metadata.Tables;

namespace Mystia.InteropGen;

/// <summary>
/// Lays out the value types of a generated interop assembly.
///
/// Il2CppInterop writes every generated value type as explicit layout and copies each field's offset out
/// of the input's <c>FieldOffsetAttribute</c> (a named <c>Offset</c> property that Il2CppDumper's dummy
/// assemblies carry). It never computes the offsets from the fields, so an input that does not carry them
/// - a plain managed backup of the game project, or a dump whose field offsets were not recovered - yields
/// a struct whose fields all sit at offset 0. The managed size of such a struct is the size of its largest
/// field: Unity's <c>Color</c> (four floats, sixteen bytes) becomes four bytes. Every il2cpp call that
/// writes such a value through a managed address (the generated constructors hand
/// <c>Unsafe.AsPointer(ref this)</c> to <c>il2cpp_runtime_invoke</c>) then writes past the managed local
/// and over the caller's frame, which the runtime reports as a stack cookie check failure (0xC0000409,
/// subcode 2, inside the method that was setting a colour).
///
/// This pass materializes the missing offsets the way the C# compiler and il2cpp lay a sequential struct
/// out, and leaves a type alone once the generator has given it offsets. A field's offset follows the
/// alignment of its type - for a nested struct that is the largest alignment among its own fields, not its
/// size, which is why a bool followed by a two-field struct of a short and two bytes starts that struct at
/// offset 2. Nothing this pass writes is ever smaller than what it replaces: an all-zero layout is the
/// smallest layout a type can have, so a repaired type can only grow.
/// </summary>
internal sealed class FieldLayoutPass
{
    private const int DefaultPack = 8;

    private readonly Dictionary<string, TypeDefinition> _types = new(StringComparer.Ordinal);
    private readonly Dictionary<TypeDefinition, (int Size, int Alignment)> _layouts = [];
    private readonly List<string> _unresolved = [];
    private readonly List<string> _flat = [];
    private int _materialized;

    private FieldLayoutPass()
    {
    }

    internal static FieldLayoutReport Materialize(IReadOnlyList<AssemblyDefinition> assemblies)
    {
        var pass = new FieldLayoutPass();
        foreach (var assembly in assemblies)
            foreach (var type in assembly.ManifestModule!.GetAllTypes())
                pass._types.TryAdd(type.FullName ?? type.Name?.ToString() ?? "", type);

        foreach (var assembly in assemblies)
            foreach (var type in assembly.ManifestModule!.GetAllTypes())
                pass.LayOut(type, []);

        foreach (var type in pass._types.Values)
        {
            var fields = type.Fields.Where(field => !field.IsStatic).ToList();
            if (type.IsValueType && !type.IsEnum && fields.Count > 1 && fields.All(field => field.FieldOffset == 0))
                pass._flat.Add(type.FullName ?? "?");
        }

        return new FieldLayoutReport(pass._materialized, pass._unresolved, pass._flat);
    }

    /// <summary>The size and alignment of a laid out value type, or null when a field of it cannot be sized.</summary>
    private (int Size, int Alignment)? LayOut(TypeDefinition type, HashSet<TypeDefinition> visiting)
    {
        if (!type.IsValueType || type.IsEnum || type.IsAbstract)
            return null;
        if (_layouts.TryGetValue(type, out var known))
            return known;

        var fields = type.Fields.Where(field => !field.IsStatic).ToList();
        if (fields.Count == 0)
            return null;

        var pack = type.ClassLayout?.PackingSize is { } declared && declared > 0 ? declared : DefaultPack;

        // The generator copies the offsets the input declared, so a type it already laid out keeps that
        // layout: Unity's Color32 overlaps its fields on purpose and is not a sequential struct. Offsets that
        // are all zero are what a missing layout looks like, and are laid out here instead.
        if (fields.Any(field => field.FieldOffset > 0))
            return Remember(type, LayOutFromOffsets(fields, pack, visiting));

        if (!visiting.Add(type))
            return null;

        var offsets = new List<(FieldDefinition Field, int Offset)>();
        var size = 0;
        var alignment = 1;
        foreach (var field in fields)
        {
            var length = SizeOf(field.Signature!.FieldType, visiting);
            if (length is not { } fieldShape)
            {
                visiting.Remove(type);
                _unresolved.Add(type.FullName ?? "?");
                return null;
            }

            var fieldAlignment = Math.Min(fieldShape.Alignment, pack);
            alignment = Math.Max(alignment, fieldAlignment);
            var offset = Align(size, fieldAlignment);
            offsets.Add((field, offset));
            size = offset + fieldShape.Size;
        }

        visiting.Remove(type);
        foreach (var (field, offset) in offsets)
            field.FieldOffset = offset;
        _materialized++;
        return Remember(type, (Align(size, alignment), alignment));
    }

    private (int Size, int Alignment)? LayOutFromOffsets(List<FieldDefinition> fields, int pack, HashSet<TypeDefinition> visiting)
    {
        var end = 0;
        var alignment = 1;
        foreach (var field in fields)
        {
            var length = SizeOf(field.Signature!.FieldType, visiting);
            if (length is not { } fieldShape)
                return null;

            end = Math.Max(end, field.FieldOffset.GetValueOrDefault() + fieldShape.Size);
            alignment = Math.Max(alignment, Math.Min(fieldShape.Alignment, pack));
        }

        return (Align(end, alignment), alignment);
    }

    private (int Size, int Alignment)? Remember(TypeDefinition type, (int Size, int Alignment)? layout)
    {
        if (layout is { } value)
            _layouts[type] = value;
        return layout;
    }

    private (int Size, int Alignment)? SizeOf(TypeSignature type, HashSet<TypeDefinition> visiting)
    {
        switch (type.ElementType)
        {
            case ElementType.Boolean:
            case ElementType.I1:
            case ElementType.U1:
                return (1, 1);
            case ElementType.Char:
            case ElementType.I2:
            case ElementType.U2:
                return (2, 2);
            case ElementType.I4:
            case ElementType.U4:
            case ElementType.R4:
                return (4, 4);
            case ElementType.I8:
            case ElementType.U8:
            case ElementType.R8:
                return (8, 8);
            case ElementType.I:
            case ElementType.U:
            case ElementType.Ptr:
            case ElementType.FnPtr:
                return (IntPtr.Size, IntPtr.Size);
        }

        // The generator rewrites a field that holds a reference to IntPtr.
        if (type is PointerTypeSignature or ByReferenceTypeSignature or SzArrayTypeSignature or ArrayTypeSignature || !type.IsValueType)
            return (IntPtr.Size, IntPtr.Size);

        // A value type this pass cannot size is not one it can lay out: a guessed size is what put the wrong
        // layout there in the first place, so the type is reported instead of approximated.
        var name = type.FullName;
        if (name is null || !_types.TryGetValue(name, out var definition))
            return null;

        if (definition.IsEnum)
        {
            var underlying = definition.GetEnumUnderlyingType();
            return underlying is null ? (4, 4) : SizeOf(underlying, visiting);
        }

        return LayOut(definition, visiting);
    }

    private static int Align(int value, int alignment) => alignment <= 1 ? value : (value + alignment - 1) / alignment * alignment;
}

internal sealed record FieldLayoutReport(int Materialized, IReadOnlyList<string> Unresolved, IReadOnlyList<string> Flat)
{
    internal string Summary() =>
        $"{Materialized} value types laid out, {Unresolved.Count} could not be sized, {Flat.Count} still carry no offset";
}
