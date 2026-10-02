namespace Mystia;

public interface IModRegistrar
{
    void Add<TContract>(TContract instance) where TContract : class;

    void Add<TContract>(string key, TContract instance) where TContract : class;
}
