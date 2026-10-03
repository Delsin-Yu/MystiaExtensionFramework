using System.IO;

namespace Mystia;

/// <summary>Per-mod storage for data the mod may rewrite. The physical location is host-defined.</summary>
public interface IModCache
{
    bool Exists(string relativePath);

    Stream OpenRead(string relativePath);

    Stream OpenWrite(string relativePath);

    TextReader OpenText(string relativePath);

    TextWriter CreateText(string relativePath);

    void Delete(string relativePath);
}

/// <summary>Read-only view of a mod's own state files, for configuration loaded during startup.</summary>
public interface IModConfigSource
{
    bool Exists(string relativePath);

    Stream OpenRead(string relativePath);

    TextReader OpenText(string relativePath);
}
