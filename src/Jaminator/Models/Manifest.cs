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
        [JsonProperty("schedule")] public ScheduleEntry? Schedule { get; set; }
    }

    public sealed class ScheduleEntry
    {
        /// <summary>"HH:MM" 24-hour, e.g. "03:00". Null/empty disables the daily auto-run.</summary>
        [JsonProperty("dailyRunAll")] public string? DailyRunAll { get; set; }

        /// <summary>Bound on how long the daily run waits for internet before giving up.</summary>
        [JsonProperty("maxNetworkWaitMinutes")] public int MaxNetworkWaitMinutes { get; set; } = 5;
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

    /// <summary>
    /// Per-architecture install plan. Carries the installer itself plus any
    /// prerequisites that must run before it (e.g. XNA before Kodu).
    /// </summary>
    public sealed class ArchEntry
    {
        /// <summary>"msi" (default), "exe", or "zip-extract".</summary>
        [JsonProperty("kind")] public string Kind { get; set; } = "msi";
        [JsonProperty("url")] public string Url { get; set; } = "";
        [JsonProperty("sha256")] public string Sha256 { get; set; } = "";
        [JsonProperty("args")] public string Args { get; set; } = "";

        // zip-extract fields
        [JsonProperty("installPath")] public string? InstallPath { get; set; }
        [JsonProperty("exeName")] public string? ExeName { get; set; }
        [JsonProperty("desktopShortcut")] public bool DesktopShortcut { get; set; }
        [JsonProperty("startMenuShortcut")] public bool StartMenuShortcut { get; set; }
        [JsonProperty("shortcutName")] public string? ShortcutName { get; set; }

        /// <summary>Prerequisites installed (in order) before this entry.</summary>
        [JsonProperty("prerequisites")] public List<ArchEntry> Prerequisites { get; set; } = new();

        /// <summary>Optional detect rule. If this matches, the entry is treated as already-installed and skipped.</summary>
        [JsonProperty("detect")] public DetectEntry? Detect { get; set; }
    }

    public sealed class DetectEntry
    {
        [JsonProperty("registryKey")] public string? RegistryKey { get; set; }
        [JsonProperty("minVersion")] public string? MinVersion { get; set; }
        [JsonProperty("appxPackageName")] public string? AppxPackageName { get; set; }
        [JsonProperty("filePath")] public string? FilePath { get; set; }
    }

    public sealed class CommandEntry
    {
        [JsonProperty("id")] public string Id { get; set; } = "";
        [JsonProperty("name")] public string Name { get; set; } = "";
        [JsonProperty("shell")] public string Shell { get; set; } = "powershell";
        [JsonProperty("script")] public string Script { get; set; } = "";

        /// <summary>
        /// PowerShell boolean expression. If it evaluates true, the command is
        /// skipped — used to make commands idempotent (e.g. "AllowCortana already 0").
        /// </summary>
        [JsonProperty("skipIf")] public string? SkipIf { get; set; }
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
