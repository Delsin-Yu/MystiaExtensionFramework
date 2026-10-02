namespace Mystia;

public interface IIl2CppComponentHost
{
    void RegisterBehaviour(Type behaviourType);

    void CreatePersistent(string name, Type behaviourType);
}
