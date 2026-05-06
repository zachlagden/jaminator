using System.Collections.Generic;
using Newtonsoft.Json;

namespace Jaminator.Models
{
    public sealed class Manifest
    {
        [JsonProperty("schemaVersion")] public int SchemaVersion { get; set; }
        [JsonProperty("manifestVersion")] public string ManifestVersion { get; set; } = "";
        [JsonProperty("minimumToolVersion")] public string MinimumToolVersion { get; set; } = "0.0.0";
        [JsonProperty("wallpaper")] public WallpaperEntry? Wallpaper { get; set; }
        [JsonProperty("folders")] public List<FolderEntry> Folders { get; set; } = new();
        [JsonProperty("programs")] public List<ProgramEntry> Programs { get; set; } = new();
        [JsonProperty("commands")] public List<CommandEntry> Commands { get; set; } = new();
        [JsonProperty("cleanup")] public CleanupEntry? Cleanup { get; set; }
    }

    public sealed class WallpaperEntry
    {
        [JsonProperty("url")] public string Url { get; set; } = "";
        [JsonProperty("sha256")] public string Sha256 { get; set; } = "";
        [JsonProperty("enforce")] public bool Enforce { get; set; }
    }

    public sealed class FolderEntry
    {
        [JsonProperty("path")] public string Path { get; set; } = "";
        [JsonProperty("createIfMissing")] public bool CreateIfMissing { get; set; } = true;
    }

    public sealed class ProgramEntry
    {
        [JsonProperty("id")] public string Id { get; set; } = "";
        [JsonProperty("name")] public string Name { get; set; } = "";
        [JsonProperty("x64")] public ArchEntry? X64 { get; set; }
        [JsonProperty("x86")] public ArchEntry? X86 { get; set; }
        [JsonProperty("detect")] public DetectEntry? Detect { get; set; }
    }

    public sealed class ArchEntry
    {
        [JsonProperty("url")] public string Url { get; set; } = "";
        [JsonProperty("sha256")] public string Sha256 { get; set; } = "";
        [JsonProperty("args")] public string Args { get; set; } = "";
    }

    public sealed class DetectEntry
    {
        [JsonProperty("registryKey")] public string? RegistryKey { get; set; }
        [JsonProperty("minVersion")] public string? MinVersion { get; set; }
        [JsonProperty("appxPackageName")] public string? AppxPackageName { get; set; }
    }

    public sealed class CommandEntry
    {
        [JsonProperty("id")] public string Id { get; set; } = "";
        [JsonProperty("name")] public string Name { get; set; } = "";
        [JsonProperty("shell")] public string Shell { get; set; } = "powershell";
        [JsonProperty("script")] public string Script { get; set; } = "";
    }

    public sealed class CleanupEntry
    {
        [JsonProperty("tempPaths")] public List<string> TempPaths { get; set; } = new();
        [JsonProperty("emptyRecycleBin")] public bool EmptyRecycleBin { get; set; }
        [JsonProperty("clearBrowserCache")] public BrowserCacheEntry? ClearBrowserCache { get; set; }
        [JsonProperty("documentsAllowlist")] public DocumentsAllowlistEntry? DocumentsAllowlist { get; set; }
        [JsonProperty("resetWallpaperIfChanged")] public bool ResetWallpaperIfChanged { get; set; }
    }

    public sealed class BrowserCacheEntry
    {
        [JsonProperty("edge")] public bool Edge { get; set; }
        [JsonProperty("chrome")] public bool Chrome { get; set; }
        [JsonProperty("firefox")] public bool Firefox { get; set; }
    }

    public sealed class DocumentsAllowlistEntry
    {
        [JsonProperty("enabled")] public bool Enabled { get; set; }
        [JsonProperty("quarantineFolder")] public string QuarantineFolder { get; set; } = "";
        [JsonProperty("allowedSubfolders")] public List<string> AllowedSubfolders { get; set; } = new();
        [JsonProperty("allowedFiles")] public List<string> AllowedFiles { get; set; } = new();
    }
}
