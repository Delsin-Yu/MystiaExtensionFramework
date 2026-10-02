using System.Text.Json.Serialization;

namespace Mystia.Modding.Host;

internal sealed class ModManifest
{
    public string Id { get; set; } = "";

    public string Version { get; set; } = "";

    public string? Assembly { get; set; }

    public string[] LoadAfter { get; set; } = [];

    [JsonIgnore]
    public string Directory { get; set; } = "";

    [JsonIgnore]
    public string AssemblyPath { get; set; } = "";
}
