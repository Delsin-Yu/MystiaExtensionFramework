namespace Mystia;

/// <summary>
/// Marks an interface the source generator discovers. Mod authors do not apply this attribute.
/// </summary>
[AttributeUsage(AttributeTargets.Interface, AllowMultiple = false, Inherited = false)]
public sealed class AutoWireAttribute : Attribute
{
}
