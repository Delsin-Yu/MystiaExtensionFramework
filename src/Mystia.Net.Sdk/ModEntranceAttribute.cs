namespace Mystia;

/// <summary>
/// Points the host at the generated entrance. The source generator emits this; mods do not.
/// </summary>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false)]
public sealed class ModEntranceAttribute(Type entranceType)
    : Attribute
{
    public Type EntranceType { get; } = entranceType;
}
