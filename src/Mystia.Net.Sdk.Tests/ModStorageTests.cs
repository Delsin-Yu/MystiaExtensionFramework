using System.Text.Json;
using System.Text.Json.Nodes;
using Mystia.Modding.Bridge;
using Mystia;
using Xunit;

namespace Mystia.Tests;

/// <summary>
/// Per-mod storage and the record list that rides in the player's own save. Both halves run without a game:
/// the storage works on the file system and the record list is pure managed, so the rules that decide what
/// a save keeps can be checked with fake records.
/// </summary>
public sealed class ModStorageTests : IDisposable
{
    private readonly string _root;

    public ModStorageTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "mystia-mod-storage-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temporary directory is not a test failure.
        }
    }

    [Fact]
    public void ConfigTextRoundTrips()
    {
        var storage = new ModStorage(_root);

        Assert.False(storage.TryOpenConfigRead("config.json", out var missing));
        Assert.Null(missing);

        WriteConfig(storage, "config.json", "{\"volume\":3}");

        Assert.Equal("{\"volume\":3}", ReadConfig(storage, "config.json"));
    }

    [Fact]
    public void ConfigWriteCreatesTheSubdirectoryItIsGiven()
    {
        var storage = new ModStorage(_root);

        WriteConfig(storage, Path.Combine("profiles", "guest.json"), "tewi");

        Assert.Equal("tewi", ReadConfig(storage, Path.Combine("profiles", "guest.json")));
    }

    [Fact]
    public void CacheBytesRoundTripAndDelete()
    {
        var storage = new ModStorage(_root);

        Assert.False(storage.Exists(Path.Combine("screenshots", "day1.png")));
        Assert.False(storage.TryOpenRead(Path.Combine("screenshots", "day1.png"), out var absent));
        Assert.Null(absent);

        Assert.True(storage.TryOpenWrite(Path.Combine("screenshots", "day1.png"), out var opened));
        using (var stream = opened!)
            stream.Write(new byte[] { 1, 2, 3 });

        Assert.True(storage.Exists(Path.Combine("screenshots", "day1.png")));
        Assert.True(storage.TryOpenRead(Path.Combine("screenshots", "day1.png"), out var source));
        using (var input = source!)
        {
            var copy = new MemoryStream();
            input.CopyTo(copy);
            Assert.Equal(new byte[] { 1, 2, 3 }, copy.ToArray());
        }

        Assert.True(storage.TryDelete(Path.Combine("screenshots", "day1.png")));
        Assert.False(storage.Exists(Path.Combine("screenshots", "day1.png")));
        // Deleting again is not an error: the file is gone either way.
        Assert.True(storage.TryDelete(Path.Combine("screenshots", "day1.png")));
    }

    [Fact]
    public void ConfigAndCacheDoNotShareANameSpace()
    {
        var storage = new ModStorage(_root);

        WriteConfig(storage, "state.json", "config");
        Assert.True(storage.TryOpenWrite("state.json", out var stream));
        using (var cache = stream!)
            cache.Write(new byte[] { 9 });

        Assert.Equal("config", ReadConfig(storage, "state.json"));
        Assert.True(storage.TryOpenRead("state.json", out var read));
        using (var cache = read!)
        {
            var copy = new MemoryStream();
            cache.CopyTo(copy);
            Assert.Equal(new byte[] { 9 }, copy.ToArray());
        }
    }

    [Fact]
    public void APathThatLeavesTheModOwnedAreaIsRefused()
    {
        var storage = new ModStorage(_root);
        var outside = Path.GetDirectoryName(_root)!;
        var escape = Path.Combine("..", "mystia-escape.bin");
        var escapeConfig = Path.Combine("..", "mystia-escape.json");

        Assert.False(storage.TryOpenWrite(escape, out var stream));
        Assert.Null(stream);
        Assert.False(storage.TryOpenRead(escape, out var read));
        Assert.Null(read);
        Assert.False(storage.Exists(escape));
        Assert.False(storage.TryDelete(escape));
        Assert.False(storage.TryOpenConfigWrite(escapeConfig, out var writer));
        Assert.Null(writer);
        Assert.False(storage.TryOpenConfigRead(escapeConfig, out var reader));
        Assert.Null(reader);

        // An absolute path is refused as well, even when it points inside the mod's own area.
        Assert.False(storage.TryOpenWrite(Path.Combine(_root, "absolute.bin"), out var absolute));
        Assert.Null(absolute);
        Assert.False(storage.TryOpenConfigWrite("", out var blank));
        Assert.Null(blank);
        Assert.False(storage.Exists(null!));

        Assert.False(File.Exists(Path.Combine(outside, "mystia-escape.bin")));
        Assert.False(File.Exists(Path.Combine(outside, "mystia-escape.json")));
    }

    [Fact]
    public void TheCarrierKeyIsTheAgreedOne()
    {
        Assert.Equal("MystiaExtensionFramework", ModSaveSeams.StorageKey);
    }

    [Fact]
    public void ReplacingOneRecordKeepsEveryOtherRecordVerbatim()
    {
        var foreign = "  {\"module\":\"somewhere.else\", \"data\":{\"handWritten\":true}}  ";
        var unreadable = "not json at all";
        var records = new ModSaveRecords(
        [
            Envelope("sample.a", "{\"count\":1}"),
            foreign,
            unreadable,
            Envelope("sample.b", "{\"count\":7}"),
        ]);

        records.Replace("sample.a", new JsonObject { ["count"] = 2 });

        var after = records.ToArray();
        Assert.Equal(4, after.Length);
        Assert.Equal(foreign, after[1]);
        Assert.Equal(unreadable, after[2]);
        Assert.Equal(Envelope("sample.b", "{\"count\":7}"), after[3]);
        Assert.Equal("{\"count\":2}", records.Data("sample.a"));
        Assert.Equal("{\"count\":7}", records.Data("sample.b"));
    }

    [Fact]
    public void AModWithoutARecordGetsOneAppended()
    {
        var records = new ModSaveRecords([Envelope("sample.a", "{\"count\":1}")]);

        records.Replace("sample.b", new JsonObject { ["count"] = 2 });

        var after = records.ToArray();
        Assert.Equal(2, after.Length);
        Assert.Equal(Envelope("sample.a", "{\"count\":1}"), after[0]);
        Assert.Equal("{\"count\":2}", records.Data("sample.b"));
    }

    [Fact]
    public void ARecordNobodyCanReadIsNoModsData()
    {
        var records = new ModSaveRecords(
        [
            "not json at all",
            "{\"module\":42,\"data\":{\"count\":1}}",
            "[{\"module\":\"sample.a\"}]",
            "{\"data\":{\"count\":1}}",
            "",
        ]);

        Assert.Null(records.Data("sample.a"));
        Assert.Null(records.Data("42"));

        // Replacing a mod that has none of these records leaves every one of them alone.
        records.Replace("sample.a", new JsonObject { ["count"] = 2 });
        Assert.Equal(6, records.ToArray().Length);
    }

    [Fact]
    public void AMissingOrNullPayloadReadsAsNoData()
    {
        var records = new ModSaveRecords(
        [
            "{\"module\":\"sample.a\"}",
            "{\"module\":\"sample.b\",\"data\":null}",
        ]);

        Assert.Null(records.Data("sample.a"));
        Assert.Null(records.Data("sample.b"));
    }

    [Fact]
    public void TheSameRecordIsFoundForAModWhoseDataIsNotAnObject()
    {
        var records = new ModSaveRecords([Envelope("sample.a", "[1,2,3]")]);

        Assert.Equal("[1,2,3]", records.Data("sample.a"));
    }

    [Fact]
    public void TheDefaultSaveBodyDeepCopiesWhatWasLoaded()
    {
        // The default body is a default interface member: it is only reachable through the interface.
        IModSaveHandler handler = new PassthroughHandler();
        var current = new JsonObject
        {
            ["nested"] = new JsonObject { ["count"] = 1 },
            ["list"] = new JsonArray(1, 2),
            ["nothing"] = null,
        };
        var target = new JsonObject();

        handler.OnModSave(current, target);

        Assert.Equal(1, (int?)target["nested"]?["count"]);
        Assert.Equal(2, target["list"]?.AsArray().Count);
        Assert.Null(target["nothing"]);
        Assert.True(target.ContainsKey("nothing"));

        // The copy is detached: changing the target leaves what was loaded alone.
        target["nested"]!["count"] = 5;
        Assert.Equal(1, (int?)current["nested"]!["count"]);
    }

    private static string Envelope(string module, string data) =>
        "{\"module\":\"" + module + "\",\"data\":" + data + "}";

    private static void WriteConfig(IModStorage storage, string relativePath, string text)
    {
        Assert.True(storage.TryOpenConfigWrite(relativePath, out var opened));
        using var writer = opened!;
        writer.Write(text);
    }

    private static string? ReadConfig(IModStorage storage, string relativePath)
    {
        if (!storage.TryOpenConfigRead(relativePath, out var opened))
            return null;
        using var reader = opened!;
        return reader.ReadToEnd();
    }

    /// <summary>A mod that persists nothing of its own and relies on the default <c>OnModSave</c> body.</summary>
    private sealed class PassthroughHandler : IModSaveHandler
    {
        public void OnModLoad(JsonDocument? toRead)
        {
        }
    }
}
