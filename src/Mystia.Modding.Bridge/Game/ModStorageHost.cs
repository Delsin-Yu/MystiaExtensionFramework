using System.Diagnostics.CodeAnalysis;
using System.IO;

namespace Mystia.Modding.Bridge;

/// <summary>
/// A mod's storage on disk. Configuration is text the player may edit and lives in the mod's own
/// directory; cache files are raw bytes in a hidden <c>.state</c> folder next to it. Both stay under the
/// root the host hands in, and a relative path that would leave that root is refused before any file is
/// touched.
/// </summary>
internal sealed class ModStorage : IModStorage
{
    private const string CacheFolder = ".state";

    private readonly string _configRoot;
    private readonly string _cacheRoot;

    internal ModStorage(string root)
    {
        _configRoot = Prepare(Path.GetFullPath(root));
        _cacheRoot = Prepare(Path.Combine(_configRoot, CacheFolder));
    }

    public bool TryOpenConfigRead(string relativePath, [NotNullWhen(true)] out TextReader? reader)
    {
        reader = null;
        if (!Resolve(_configRoot, relativePath, out var path) || !File.Exists(path))
            return false;
        try
        {
            reader = new StreamReader(path);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    public bool TryOpenConfigWrite(string relativePath, [NotNullWhen(true)] out TextWriter? writer)
    {
        writer = null;
        if (!Resolve(_configRoot, relativePath, out var path))
            return false;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            writer = new StreamWriter(File.Create(path));
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    public bool Exists(string relativePath) => Resolve(_cacheRoot, relativePath, out var path) && File.Exists(path);

    public bool TryOpenRead(string relativePath, [NotNullWhen(true)] out Stream? stream)
    {
        stream = null;
        if (!Resolve(_cacheRoot, relativePath, out var path) || !File.Exists(path))
            return false;
        try
        {
            stream = File.OpenRead(path);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    public bool TryOpenWrite(string relativePath, [NotNullWhen(true)] out Stream? stream)
    {
        stream = null;
        if (!Resolve(_cacheRoot, relativePath, out var path))
            return false;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            stream = File.Create(path);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    public bool TryDelete(string relativePath)
    {
        if (!Resolve(_cacheRoot, relativePath, out var path))
            return false;
        try
        {
            if (File.Exists(path))
                File.Delete(path);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static string Prepare(string path)
    {
        Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>
    /// Resolves a relative path inside one root, or reports false when it is blank, absolute, malformed or
    /// would leave the root. The check runs on the full path, so <c>..</c> segments cannot escape either.
    /// </summary>
    private static bool Resolve(string root, string relativePath, out string path)
    {
        path = "";
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
            return false;

        string full;
        try
        {
            full = Path.GetFullPath(Path.Combine(root, relativePath));
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (NotSupportedException)
        {
            return false;
        }
        catch (PathTooLongException)
        {
            return false;
        }

        var prefix = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;
        if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return false;

        path = full;
        return true;
    }
}
