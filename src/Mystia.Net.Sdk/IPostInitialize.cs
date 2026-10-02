namespace Mystia;

[AutoWire]
public interface IPostInitialize
{
    void PostInitialize(IModContext context);
}
