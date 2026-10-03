using System.IO;

namespace Mystia.Modding.Bridge;

internal sealed class ModStorage : IModCache, IModConfigSource
{
    private readonly string _root;

    internal ModStorage(string root)
    {
        _root = Path.GetFullPath(root);
        Directory.CreateDirectory(_root);
    }

    public bool Exists(string relativePath) => File.Exists(Resolve(relativePath));

    public Stream OpenRead(string relativePath) => File.OpenRead(Resolve(relativePath));

    public Stream OpenWrite(string relativePath)
    {
        var path = Resolve(relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        return File.Create(path);
    }

    public TextReader OpenText(string relativePath) => new StreamReader(OpenRead(relativePath));

    public TextWriter CreateText(string relativePath) => new StreamWriter(OpenWrite(relativePath));

    public void Delete(string relativePath)
    {
        var path = Resolve(relativePath);
        if (File.Exists(path))
            File.Delete(path);
    }

    private string Resolve(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
            throw new ArgumentException("A relative path is required.", nameof(relativePath));

        var path = Path.GetFullPath(Path.Combine(_root, relativePath));
        var prefix = _root.EndsWith(Path.DirectorySeparatorChar) ? _root : _root + Path.DirectorySeparatorChar;
        if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"'{relativePath}' leaves the mod storage directory.", nameof(relativePath));
        return path;
    }
}
