namespace Mystia.Modding.Bridge;

internal static class GamePatches
{
#pragma warning disable CS0649
    internal static Action<string>? InstallOverride;
    internal static Action<Type>? RegisterOverride;
    internal static Action<string, Type>? CreateOverride;
#pragma warning restore CS0649

    internal static void TryInstall(string gameRoot) => InstallOverride?.Invoke(gameRoot);

    internal static void RegisterBehaviour(Type behaviourType)
    {
        if (RegisterOverride is null)
            throw new InvalidOperationException($"IL2CPP behaviour '{behaviourType.FullName}' cannot be registered before the game bridge is installed.");
        RegisterOverride(behaviourType);
    }

    internal static void CreatePersistent(string name, Type behaviourType)
    {
        if (CreateOverride is null)
            throw new InvalidOperationException($"IL2CPP object '{name}' cannot be created before the game bridge is installed.");
        CreateOverride(name, behaviourType);
    }
}
