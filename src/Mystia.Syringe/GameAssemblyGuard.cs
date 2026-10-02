using System.Security.Cryptography;

namespace Mystia.Syringe;

internal static class GameAssemblyGuard
{
    public const string ExpectedReleaseHash = "91CE5AE3DAD5DA07DFED63BAB4C9E454F67B6E50F9A6E8EC498EF9B0B806A789";

    public static GuardResult Check(string gameExe, string expectedHash)
    {
        if (string.IsNullOrWhiteSpace(gameExe) || !File.Exists(gameExe))
            return new GuardResult(false, "", $"Game executable was not found: {gameExe}");

        var assemblyPath = Path.Combine(Path.GetDirectoryName(gameExe)!, "GameAssembly.dll");
        if (!File.Exists(assemblyPath))
            return new GuardResult(false, "", $"GameAssembly.dll was not found beside '{gameExe}'.");

        var actual = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(assemblyPath)));
        if (!string.Equals(actual, expectedHash, StringComparison.OrdinalIgnoreCase))
        {
            return new GuardResult(
                false,
                actual,
                $"GameAssembly.dll hash {actual} does not match the interop manifest {expectedHash}. The game was not started.");
        }

        return new GuardResult(true, actual, "");
    }
}

internal readonly record struct GuardResult(bool Ok, string ActualHash, string Message);
