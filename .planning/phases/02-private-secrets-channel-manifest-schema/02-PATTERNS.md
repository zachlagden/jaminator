# Phase 2: Private secrets channel + manifest schema - Pattern Map

**Mapped:** 2026-05-11
**Files analyzed:** 10 (5 modify, 5 create — one of the 10 is verify-only)
**Analogs found:** 8 / 10 (2 files have no direct in-tree analog; both use RESEARCH.md canonical templates instead)

## File Classification

| New/Modified File | Action | Role | Data Flow | Closest Analog | Match Quality |
|-------------------|--------|------|-----------|----------------|---------------|
| `src/Jaminator/Models/Manifest.cs` | modify | model (DTO) | transform (JSON → C# objects) | `CleanupEntry` / `WallpaperEntry` / `ScheduleEntry` already in the same file | exact |
| `src/Jaminator/Services/ManifestFetcher.cs` | modify | service | request-response + file-I/O (cache) | existing `FetchAsync` in the same file | exact |
| `src/Jaminator/Program.cs` | modify | entry point | logging (post-fetch debug emit) | existing `Logger` consumer pattern in `Program.cs` headless branch | role-match |
| `src/Jaminator/Generated/BuildSecrets.g.cs` | create | generated-code (constants) | static (compile-time inline) | no in-tree analog — file is `.gitignore`'d by design | RESEARCH.md template |
| `src/Jaminator/Jaminator.csproj` | verify-only | config | n/a (build) | current csproj already SDK-style; verification is a `dotnet msbuild -preprocess` check, no edit required | n/a |
| `installer/build.ps1` | modify | build script | file-I/O + env-var read | existing version-discovery + EXE-presence-check block in same file | exact |
| `installer/secrets/.keep` | create | marker file | n/a (empty) | repo has no analog empty-directory markers; trivial 0-byte file | trivial |
| `installer/secrets/README.md` | create | operator doc | n/a (markdown) | no operator-runbook README in repo today | RESEARCH.md outline |
| `.gitignore` | modify | config | n/a | existing `.gitignore` rules — append-only edit | exact |
| `docs/manifest-schema.md` | modify | docs | n/a (markdown) | existing `docs/manifest-schema.md` schema block style | exact |

## Pattern Assignments

### `src/Jaminator/Models/Manifest.cs` (model, transform — modify)

**Analog:** same file — `WallpaperEntry`, `CleanupEntry`, `ScheduleEntry`, `CommandEntry` already on disk.

**Imports pattern** (lines 1-2 — already in file, no change needed):
```csharp
using System.Collections.Generic;
using Newtonsoft.Json;
```

**Top-level wrapper field on `Manifest`** — copy the `Wallpaper` / `Cleanup` / `Schedule` shape (lines 11-16):
```csharp
[JsonProperty("wallpaper")] public WallpaperEntry? Wallpaper { get; set; }
[JsonProperty("folders")] public List<FolderEntry> Folders { get; set; } = new();
[JsonProperty("programs")] public List<ProgramEntry> Programs { get; set; } = new();
[JsonProperty("commands")] public List<CommandEntry> Commands { get; set; } = new();
[JsonProperty("cleanup")] public CleanupEntry? Cleanup { get; set; }
[JsonProperty("schedule")] public ScheduleEntry? Schedule { get; set; }
```
The new line is `[JsonProperty("wifi")] public WifiEntry? Wifi { get; set; }` — nullable wrapper, no default initialiser, sitting alongside the others.

**Sealed-wrapper-with-list pattern** — copy `CleanupEntry` (lines 98-105) for `WifiEntry`'s shape:
```csharp
public sealed class CleanupEntry
{
    [JsonProperty("tempPaths")] public List<string> TempPaths { get; set; } = new();
    [JsonProperty("emptyRecycleBin")] public bool EmptyRecycleBin { get; set; }
    [JsonProperty("clearBrowserCache")] public BrowserCacheEntry? ClearBrowserCache { get; set; }
    [JsonProperty("documentsAllowlist")] public DocumentsAllowlistEntry? DocumentsAllowlist { get; set; }
    [JsonProperty("resetWallpaperIfChanged")] public bool ResetWallpaperIfChanged { get; set; }
}
```
`WifiEntry` mirrors this: `public sealed class`, single `Profiles: List<WifiProfileEntry>` field with `= new()` default, snake-case `[JsonProperty("profiles")]`.

**Leaf-entry pattern with defaulted-string fields and XML docs** — copy `CommandEntry` (lines 84-96) for `WifiProfileEntry`:
```csharp
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
```
Note three patterns this analog establishes:
1. **String-typed schema fields with enum-like documented values** — `Shell` defaults to `"powershell"`; the runner accepts `"powershell"` or `"cmd"`. `WifiProfileEntry.AuthMode` (`"WPA2PSK"` / `"WPA3PSK"` / `"open"`) and `Scope` (`"all-users"` / `"current-user"`) follow this exact convention.
2. **Nullable `string?`** for optional fields — `SkipIf` is the precedent for `Psk`.
3. **XML `/// <summary>` block** above non-obvious fields — use one for `Psk` to call out "never set from public manifest; populated at runtime from private channel".

**Canonical literal `WifiProfileEntry` + `WifiEntry`** (lifted verbatim from RESEARCH.md §"Pattern: WifiEntry + WifiProfileEntry DTOs", lines 530-567):
```csharp
public sealed class WifiEntry
{
    [JsonProperty("profiles")] public List<WifiProfileEntry> Profiles { get; set; } = new();
}

public sealed class WifiProfileEntry
{
    [JsonProperty("ssid")] public string Ssid { get; set; } = "";
    [JsonProperty("authMode")] public string AuthMode { get; set; } = "WPA2PSK";
    [JsonProperty("hidden")] public bool Hidden { get; set; }
    [JsonProperty("autoConnect")] public bool AutoConnect { get; set; } = true;
    [JsonProperty("scope")] public string Scope { get; set; } = "all-users";
    [JsonProperty("psk")] public string? Psk { get; set; }
}
```
Note: per CONTEXT.md D-15, the `ToString()` override is **deferred to Phase 3** (it's referenced in D-15 but the actual override lives in the phase where the runner starts string-interpolating). RESEARCH.md line 558-559 shows the canonical override body; do not emit it in Phase 2 unless the planner decides to land it pre-emptively (CONTEXT.md leaves this as Claude's Discretion).

---

### `src/Jaminator/Services/ManifestFetcher.cs` (service, request-response + file-I/O — modify)

**Analog:** same file — existing `FetchAsync` (lines 36-67) is the structural template.

**Imports pattern to extend** (lines 1-6) — add `System.Collections.Generic` (for `Dictionary<string,string>`), `System.Net.Http.Headers` (for `AuthenticationHeaderValue` and `MediaTypeWithQualityHeaderValue`), `System.Linq` is not needed:
```csharp
using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Jaminator.Models;
using Newtonsoft.Json;
```

**Static `HttpClient` field — reuse, do not duplicate** (lines 18-21):
```csharp
private static readonly HttpClient Http = new HttpClient
{
    Timeout = TimeSpan.FromSeconds(15)
};
```
CONTEXT.md D-13 locks this in. Both fetches go through the single `Http` instance. The bearer header is per-request via `HttpRequestMessage` (RESEARCH.md Pattern 4).

**Cache-path helper pattern** (lines 23-33) — copy this idiom for a second `SecretsCachePath`:
```csharp
private static string CachePath
{
    get
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Jaminator", "cache");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "manifest.json");
    }
}
```
Add a parallel `SecretsCachePath` that produces `…\Jaminator\cache\secrets.json`. `Directory.CreateDirectory` is idempotent so calling it twice is fine. Alternative: refactor to a single `CacheDir` getter + two file-name constants — planner's choice (cosmetic, no behaviour difference).

**Cache-busting query string pattern** (line 40):
```csharp
var bust = $"?t={DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";
var json = await Http.GetStringAsync(url + bust).ConfigureAwait(false);
```
Apply the same `?t=<unix>` suffix to the private fetch URL. **Important:** the private URL already has a query string (`?ref=main`) so the buster must be `&t=…` not `?t=…`. RESEARCH.md line 653 shows this distinction.

**Public-fetch deserialise + cache pattern** (lines 41-45) — the structural skeleton:
```csharp
var m = JsonConvert.DeserializeObject<Manifest>(json)
        ?? throw new InvalidOperationException("Manifest deserialised to null");
try { File.WriteAllText(CachePath, json); } catch { /* cache write best-effort */ }
return (m, fromCache: false);
```
Extend this skeleton: **after** the public deserialise, do the private fetch, deserialise as `Dictionary<string, string>`, call `JoinPsks`, then write the **pair** atomically (RESEARCH.md Pattern 3). The `try { WriteAll } catch { /* best-effort */ }` idiom carries over for the pair: cache failures must never crash the fetch.

**Hierarchical-fallback exception pattern** (lines 47-66) — the canonical extension target:
```csharp
catch (Exception netEx)
{
    if (File.Exists(CachePath))
    {
        try
        {
            var json = File.ReadAllText(CachePath);
            var m = JsonConvert.DeserializeObject<Manifest>(json)
                    ?? throw new InvalidOperationException("Cached manifest deserialised to null");
            return (m, fromCache: true);
        }
        catch (Exception cacheEx)
        {
            throw new InvalidOperationException(
                $"Network fetch failed ({netEx.Message}) and cache is corrupt ({cacheEx.Message})", netEx);
        }
    }
    throw new InvalidOperationException(
        $"Network fetch failed and no cached manifest exists: {netEx.Message}", netEx);
}
```
Extend the predicate to `File.Exists(CachePath) && File.Exists(SecretsCachePath)` — CONTEXT.md D-11 mandates that **both** cached files must exist or the call throws. Extend the deserialise step to load both, call `JoinPsks`, and return `(manifest, fromCache: true)`. Extend the error messages to identify whether the cached **public manifest** or the cached **secrets** file was corrupt (CONTEXT.md D-11 specifies "names which file failed and why").

**Canonical dual-fetch + join body** — RESEARCH.md §"Pattern: Dual-fetch + join in ManifestFetcher.FetchAsync" (lines 642-711) is the literal scaffold the implementation should follow. Three new private helpers belong inside the class:

1. `FetchSecretsWithBearerAsync(string url, string pat, string userAgent)` — RESEARCH.md Pattern 4 (lines 380-391) is the literal body.
2. `JoinPsks(Manifest manifest, Dictionary<string, string> secrets)` — RESEARCH.md lines 699-711.
3. `WriteCachedPair(string publicJson, string secretsJson)` — atomic `.tmp` + `Move` pattern from RESEARCH.md Pattern 3 (lines 322-354), with the **.NET 4.8 quirk**: use `File.Delete` (no-op if missing) + `File.Move(tmp, dest)` since `File.Move(string, string, bool)` is .NET Core 3.0+ only.

**Bearer header construction** — RESEARCH.md Pattern 4 (lines 380-391):
```csharp
private static async Task<string> FetchWithBearerAsync(string url, string bearerToken, string userAgent)
{
    using var req = new HttpRequestMessage(HttpMethod.Get, url);
    req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
    req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github.raw+json"));
    req.Headers.UserAgent.ParseAdd(userAgent);
    req.Headers.Add("X-GitHub-Api-Version", "2026-03-10");

    using var resp = await Http.SendAsync(req).ConfigureAwait(false);
    resp.EnsureSuccessStatusCode();
    return await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
}
```
CONTEXT.md D-13/D-14 lock: per-request bearer (never on shared `DefaultRequestHeaders`), `User-Agent: Jaminator/{Program.ToolVersion}`. `using var` C# 8 syntax works under `<LangVersion>latest</LangVersion>` (verified against csproj line 12).

**Atomic-pair-write body** (.NET 4.8-compatible) — RESEARCH.md Pattern 3 lines 348-354:
```csharp
File.WriteAllText(manifestTmp, publicJson);
File.WriteAllText(secretsTmp, secretsJson);
if (File.Exists(manifestPath)) File.Delete(manifestPath);
File.Move(manifestTmp, manifestPath);
if (File.Exists(secretsPath)) File.Delete(secretsPath);
File.Move(secretsTmp, secretsPath);
```
Wrap in `try { … } catch { /* best-effort */ }` (matches the existing `WriteAllText` cache attempt). The "between-the-two-Moves" rare-failure mode is documented in RESEARCH.md line 367 and accepted for v0.8.0.

**Return-type contract** — unchanged. CONTEXT.md D-12: still `(Manifest manifest, bool fromCache)`. `fromCache: true` means **both** files came from disk (never "half from cache").

---

### `src/Jaminator/Program.cs` (entry point, logging — modify)

**Analog:** same file — existing `Logger` consumer pattern (lines 36-37):
```csharp
var log = new Logger();
log.OnMessage += line => Console.WriteLine(line);
```
This is how the headless branch instantiates a logger and wires console echo. The UI branch currently does **not** wire a `Logger` here (it's done inside `MainForm` per the OnLoad path). RESEARCH.md line 770 explicitly flags this: "Program.Main doesn't currently instantiate Logger for UI mode … The cleanest insertion point may actually be MainForm.OnLoad() immediately after the fetch returns."

**Canonical debug-log line** — RESEARCH.md §"Pattern: Debug log emission site" (lines 752-768):
```csharp
var (manifest, fromCache) = await fetcher.FetchAsync(ManifestUrl).ConfigureAwait(false);

if (manifest.Wifi != null && manifest.Wifi.Profiles.Count > 0)
{
    var ssidList = string.Join(", ", manifest.Wifi.Profiles.Select(p => p.Ssid));
    log.Info($"Joined manifest: {manifest.Wifi.Profiles.Count} Wi-Fi profile(s) — [{ssidList}] (PSKs: ***){(fromCache ? " (from cache)" : "")}");
}
else
{
    log.Info($"Joined manifest: 0 Wi-Fi profile(s){(fromCache ? " (from cache)" : "")}");
}
```
PSK masking: literal `***` per CONTEXT.md D-15.

**Logger call-site shape** (from `Logger.cs` lines 21-24):
```csharp
public void Info(string msg) => Write("INFO", msg);
public void Warn(string msg) => Write("WARN", msg);
public void Error(string msg) => Write("ERROR", msg);
public void Error(string msg, Exception ex) => Write("ERROR", msg + " — " + ex.Message);
```
Use `log.Info(…)` for the debug line (CONTEXT.md "Joined-profile debug log line wording": "Logged at `Info` level"). Output lands in `%ProgramData%\Jaminator\logs\jaminator-YYYYMMDD.log` via `Logger.Write` (line 26-37 in `Logger.cs`).

**Placement decision (Claude's Discretion per CONTEXT.md):** RESEARCH.md line 770 lists three candidate insertion points: (a) `Program.Main` UI-branch (requires instantiating a `Logger` there), (b) `MainForm.OnLoad` immediately after fetch returns, (c) a slim wrapper service called by both. The planner picks. Commit `D-16 step 5` is scoped to `src/Jaminator/Program.cs` though, which implies CONTEXT.md's intended site is **(a)** — Program.Main, with a Logger instance materialised there alongside the existing headless-branch one. The planner may adjust if the implementation reads cleaner elsewhere.

---

### `src/Jaminator/Generated/BuildSecrets.g.cs` (generated code — create)

**Analog:** none in-tree (file is `.gitignore`'d by design). Use the RESEARCH.md canonical template verbatim.

**Canonical file body** — RESEARCH.md §"Pattern: Generated BuildSecrets.g.cs" (lines 572-583):
```csharp
// Auto-generated by installer/build.ps1 at build time. Do not edit; do not commit.
// Contents are gitignored. Rotation: replace installer/secrets/wifi-pat.txt and rebuild.
namespace Jaminator
{
    internal static class BuildSecrets
    {
        internal const string WifiPat = "github_pat_11ABCDEFG...redacted...";
        internal const string SecretsUrl = "https://api.github.com/repos/jamcoding-internal/jaminator-secrets/contents/secrets.json?ref=main";
    }
}
```

**CONTEXT.md decisions to honour:**
- D-06: file path is `src/Jaminator/Generated/BuildSecrets.g.cs`; namespace is `Jaminator` (root namespace per `Jaminator.csproj` line 9); class is `internal static class BuildSecrets`; fields are `internal const string`.
- D-07: `internal const` (not `static readonly`) — IL inlining is intentional. Threat model is operational opacity, not cryptographic protection.
- The two `const` placeholders are `WifiPat` and `SecretsUrl`.

**No imports needed.** The file uses only `string` literals — no `using` directives required.

---

### `src/Jaminator/Jaminator.csproj` (config — verify only, no edit expected)

**Analog:** the file itself.

**Current SDK declaration** (line 1):
```xml
<Project Sdk="Microsoft.NET.Sdk">
```
`Microsoft.NET.Sdk` auto-globs `**/*.cs` (RESEARCH.md Pattern 2). RESEARCH.md line 511 lists the verification: run `dotnet msbuild -preprocess` and confirm `<Compile Include="Generated\BuildSecrets.g.cs" />` appears in the preprocessed output. **No csproj edits if the auto-glob is in effect.**

**Anti-pattern explicitly forbidden** — RESEARCH.md line 285 and 424: do NOT add `<Compile Include="Generated/**/*.cs" />`. It triggers NETSDK1022 (duplicate items) because the SDK auto-glob already covers it.

**Fallback if auto-glob is not picking up** `Generated/`:
```xml
<ItemGroup>
  <Compile Include="Generated/**/*.cs" />
</ItemGroup>
```
…but only if the verification step shows the file is missing. Per CONTEXT.md "Claude's Discretion", planner picks after running the verification.

**`<LangVersion>latest</LangVersion>`** (line 12) is already set, so C# 8 `using var` declarations (used in `ManifestFetcher`'s new bearer helper) compile cleanly. No csproj change needed for that.

---

### `installer/build.ps1` (build script — modify)

**Analog:** same file — existing version-discovery and EXE-presence-check blocks.

**Imports / preamble pattern** (lines 1-16) — header comment and `$ErrorActionPreference = 'Stop'` plus `$repoRoot` discovery:
```powershell
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
Push-Location $repoRoot
try {
```
No change needed; new code drops inside the existing `try { }` block.

**Pattern: file-then-throw, with concrete error message** (lines 18-23) — the version-discovery block:
```powershell
$programCs = Get-Content -Raw "$repoRoot\src\Jaminator\Program.cs"
if ($programCs -notmatch 'ToolVersion\s*=\s*"([\d.]+)"') {
    throw "Could not parse ToolVersion from Program.cs"
}
$version = $Matches[1]
```
This is the in-repo template for "read a build input, validate, throw with a concrete actionable message if invalid". The PAT/URL resolution block follows the same shape: `Test-Path` → `Get-Content -Raw` → `.Trim()` → fallback to env var → `throw` with operator-facing fix steps.

**Pattern: pre-build conditional step** (lines 27-32) — the EXE-presence-check block:
```powershell
$binDir = "$repoRoot\src\Jaminator\bin\$Configuration\net48"
if (-not (Test-Path "$binDir\Jaminator.exe")) {
    Write-Host "Building solution..."
    dotnet build "$repoRoot\Jaminator.sln" -c $Configuration | Out-Host
}
```
This is the template for "do work before `dotnet build` invocation, gated on file existence". CONTEXT.md D-06 mandates the PAT-resolution + `BuildSecrets.g.cs` write happens **before** `dotnet build`. The new code goes **above line 27** so the generated file exists when the conditional `dotnet build` kicks in.

**Canonical PAT-resolution + generation block** — RESEARCH.md §"Pattern: PowerShell PAT resolution + file generation in build.ps1" (lines 590-635):
```powershell
# --- Resolve PAT ---
$patFile = "$repoRoot\installer\secrets\wifi-pat.txt"
$urlFile = "$repoRoot\installer\secrets\wifi-secrets-url.txt"

if (Test-Path $patFile) {
    $wifiPat = (Get-Content -Raw $patFile).Trim()
} elseif ($env:JAMINATOR_WIFI_PAT) {
    $wifiPat = $env:JAMINATOR_WIFI_PAT.Trim()
} else {
    throw "installer/secrets/wifi-pat.txt not found AND `$env:JAMINATOR_WIFI_PAT not set — cannot embed Wi-Fi PAT. See installer/secrets/README.md for setup."
}

if (Test-Path $urlFile) {
    $secretsUrl = (Get-Content -Raw $urlFile).Trim()
} elseif ($env:JAMINATOR_WIFI_SECRETS_URL) {
    $secretsUrl = $env:JAMINATOR_WIFI_SECRETS_URL.Trim()
} else {
    throw "installer/secrets/wifi-secrets-url.txt not found AND `$env:JAMINATOR_WIFI_SECRETS_URL not set — cannot embed Wi-Fi secrets URL. See installer/secrets/README.md for setup."
}

# --- Generate BuildSecrets.g.cs ---
$generatedDir = "$repoRoot\src\Jaminator\Generated"
$generatedFile = "$generatedDir\BuildSecrets.g.cs"
New-Item -ItemType Directory -Path $generatedDir -Force | Out-Null

$escapedPat = $wifiPat -replace '"', '\"'
$escapedUrl = $secretsUrl -replace '"', '\"'

$content = @"
// Auto-generated by installer/build.ps1 at build time. Do not edit; do not commit.
// Contents are gitignored. Rotation: replace installer/secrets/wifi-pat.txt and rebuild.
namespace Jaminator
{
    internal static class BuildSecrets
    {
        internal const string WifiPat = "$escapedPat";
        internal const string SecretsUrl = "$escapedUrl";
    }
}
"@
Set-Content -Path $generatedFile -Value $content -NoNewline -Encoding UTF8
Write-Host "Generated $generatedFile" -ForegroundColor Cyan
```

**Pattern: success Write-Host with colour** (line 51 of existing `build.ps1`):
```powershell
Write-Host "Built $msi ($size MB)" -ForegroundColor Green
```
Use `-ForegroundColor Cyan` for the "Generated …" line so the build operator sees the generation step distinctly from the MSI-built line (Green) and from informational `Write-Host` (default colour).

**Critical: do not log the PAT or URL values.** Only log "Generated <path>". The PAT value must never appear in `Write-Host` / `Write-Verbose` output (RESEARCH.md Pitfall 5, line 501-509).

---

### `installer/secrets/.keep` (marker file — create)

**Analog:** none — repo has no analog empty-directory markers today.

**File contents:** empty (0 bytes).

**Purpose:** keeps `installer/secrets/` in version control so the directory exists on fresh clone; the `.gitignore` rule `installer/secrets/*` + `!installer/secrets/.keep` negation surfaces it. CONTEXT.md D-05.

**Create command:** `git add -f installer/secrets/.keep` (per CONTEXT.md D-05 "empty, `git add -f`'d"). The `-f` is required because the directory pattern in `.gitignore` excludes the file by default until the negation rule sees it.

---

### `installer/secrets/README.md` (operator doc — create)

**Analog:** no operator-runbook README in repo today. The closest existing analog is the prose style of `docs/manifest-schema.md` (the documentation patterns repo-wide).

**Content outline** (from CONTEXT.md `<specifics>` lines 213-215, and `<decisions>` D-05):
1. **What to drop here:**
   - `wifi-pat.txt` — single line, fine-grained PAT (`github_pat_…`), no surrounding whitespace.
   - `wifi-secrets-url.txt` — single line, full GitHub REST API contents URL, e.g. `https://api.github.com/repos/jamcoding-internal/jaminator-secrets/contents/secrets.json?ref=main`.
2. **PAT permissions required:** Contents: Read on the private secrets repo only. Nothing else. (Metadata: Read is auto-included by GitHub.)
3. **Rotation cadence:** each term, aligned with PSK rotation.
4. **Where this directory is gitignored:** `installer/secrets/*` with `.keep` and `README.md` negated.
5. **Env-var fallback:** `$env:JAMINATOR_WIFI_PAT` and `$env:JAMINATOR_WIFI_SECRETS_URL` used when the files are absent (e.g., in CI).
6. **Threat-model honesty pointer:** link to `.planning/PROJECT.md` Key Decisions (WIFI-03 row) — PAT in MSI is operational opacity, not cryptographic protection. An attacker with physical fleet access can recover it; this is accepted.
7. **Cross-link:** `docs/manifest-schema.md` §Private secrets channel.

Target audience: Jam Coding build operator. Not students/teachers.

**No code blocks beyond a 3-line example** of the two files' contents (no real PATs).

---

### `.gitignore` (config — modify)

**Analog:** same file. Current content (30 lines).

**Existing pattern style** (lines 29-30):
```gitignore
# Installer staging — too big for git, uploaded as Release assets
/installers-staging/
```
Comment-then-rule blocks with a leading `# blank line` separator.

**Canonical append block** — RESEARCH.md §"Pattern: gitignore additions" (lines 731-744):
```gitignore
# Wi-Fi secrets channel — never commit the PAT or the private secrets URL
installer/secrets/*
!installer/secrets/.keep
!installer/secrets/README.md

# Generated build-time secrets — written by installer/build.ps1
src/Jaminator/Generated/
```

**Critical git semantic** (RESEARCH.md line 748): the trailing `/*` on `installer/secrets/*` is essential. `installer/secrets/` (without `/*`) would exclude the directory entirely and the `!` negations would not work — "It is not possible to re-include a file if a parent directory of that file is excluded."

---

### `docs/manifest-schema.md` (docs — modify)

**Analog:** same file — existing schema blocks for `wallpaper`, `programs`, `commands`, `cleanup`, `schedule`. The planner should read the current file's section headings and field-table style before drafting the additions, to match the existing voice.

**Content additions** (per CONTEXT.md `<specifics>` lines 213-215 and `<decisions>` D-01/D-03):

1. **New section: Public manifest `wifi.profiles[]`** — under the public-manifest schema area:
   - Document the wrapper: `"wifi": { "profiles": [ … ] }`.
   - For each `WifiProfileEntry` field, document: name, type, default, allowed values (for string-enum fields), and whether required.
   - Explicit callout: "PSKs never appear in the public manifest. The `psk` field is reserved for runtime population from the private secrets channel."

2. **New section: Private secrets (`secrets.json` in `jamcoding-internal/jaminator-secrets`, PAT-gated)**:
   - Schema: flat JSON object, keys = SSID strings (case-sensitive), values = PSK strings.
   - Example: `{ "TestSSID": "TestPSK", "SchoolNet-Year3": "..." }`.
   - PAT-bearer mechanism: REST API contents endpoint, `Authorization: Bearer <PAT>`, `Accept: application/vnd.github.raw+json`, `User-Agent: Jaminator/<version>`, `X-GitHub-Api-Version: 2026-03-10`.
   - PAT scope: Contents: Read on the private repo only.
   - URL form: `https://api.github.com/repos/{owner}/{repo}/contents/{path}?ref={branch}`.
   - Operator setup workflow: link to `installer/secrets/README.md`.

3. **New section: Cache topology**:
   - `%ProgramData%\Jaminator\cache\manifest.json` (existing).
   - `%ProgramData%\Jaminator\cache\secrets.json` (new).
   - Atomic-pair write semantics + the "between-two-Moves" rare-failure window (one-paragraph honesty note from RESEARCH.md line 367).

4. **New section: Threat model honesty note**:
   - PAT in MSI is operational opacity, not cryptographic protection.
   - Attacker with physical fleet access can recover the PAT (and PSKs).
   - Accepted per PROJECT.md WIFI-03 Key Decisions.
   - Mitigation = rotation cadence + read-only PAT scope (blast radius bounded to the private secrets repo).

**Style match:** match the existing `docs/manifest-schema.md` heading levels, field-table style (likely `| Field | Type | Default | Notes |` markdown tables), and code-fence language tags (`json`, `csharp`, `http` for the auth header block).

---

## Shared Patterns

### Sealed-DTO + `[JsonProperty(snake_case)]`
**Source:** `src/Jaminator/Models/Manifest.cs` — every existing DTO class.
**Apply to:** `WifiEntry`, `WifiProfileEntry`.
```csharp
public sealed class WallpaperEntry
{
    [JsonProperty("url")] public string Url { get; set; } = "";
    [JsonProperty("sha256")] public string Sha256 { get; set; } = "";
    [JsonProperty("enforce")] public bool Enforce { get; set; }
}
```
Rules: `public sealed class`, every property `{ get; set; }` (no init-only), strings default to `""`, nullable strings default to `null`, lists default to `= new()`, bool defaults to `false` (implicit). Nullable reference types are enabled project-wide (csproj line 13).

### Best-effort cache writes never crash the fetch
**Source:** `ManifestFetcher.FetchAsync` line 44:
```csharp
try { File.WriteAllText(CachePath, json); } catch { /* cache write best-effort */ }
```
**Apply to:** the new atomic-pair write in `ManifestFetcher`. Wrap the full `WriteAllText` + `Delete` + `Move` sequence in a single `try { } catch { /* best-effort */ }`. Cache failures are logged-only, never fatal — the fetch must still return the in-memory joined `Manifest` to the caller.

### `InvalidOperationException` with "what failed AND fallback status" message
**Source:** `ManifestFetcher.FetchAsync` lines 60-65:
```csharp
throw new InvalidOperationException(
    $"Network fetch failed ({netEx.Message}) and cache is corrupt ({cacheEx.Message})", netEx);
```
**Apply to:** the joined-cache fallback in the extended `FetchAsync`. CONTEXT.md D-11 specifies: name **which file** (public manifest vs private secrets) failed and **why** (network vs cache). Same exception type, same message-with-inner-exception shape.

### Logger.Info for run-flow milestones; never log secret values
**Source:** `Logger.cs` lines 21-24 (signatures) + `ManifestFetcher.cs` line 44 implicit (no logger inside the fetcher currently).
**Apply to:** the post-fetch debug line in `Program.cs` (PSK masked as `***`); any `Logger.Info` call in extended `ManifestFetcher` (if added — none currently emit). Never log `BuildSecrets.WifiPat`, never log a raw secrets-JSON body, never log `WifiProfileEntry.Psk` literally.

### `Push-Location $repoRoot` + `try { … } finally { Pop-Location }` for build scripts
**Source:** `installer/build.ps1` lines 15-16, 53-55:
```powershell
$repoRoot = Split-Path -Parent $PSScriptRoot
Push-Location $repoRoot
try {
    # work
}
finally {
    Pop-Location
}
```
**Apply to:** the new PAT-resolution + generation block. It drops **inside** the existing `try { }` so the cwd stays at `$repoRoot` and the `finally { Pop-Location }` covers cleanup even if the throw triggers.

### `$ErrorActionPreference = 'Stop'` + concrete-message `throw`
**Source:** `installer/build.ps1` line 14 and 21:
```powershell
$ErrorActionPreference = 'Stop'
# ...
throw "Could not parse ToolVersion from Program.cs"
```
**Apply to:** the new PAT-missing and URL-missing throws. Message must be actionable: name the file, name the env var, link to `installer/secrets/README.md` for the fix steps. RESEARCH.md line 599 and 607 give the canonical wording.

---

## No Analog Found

| File | Role | Reason | Substitute Source |
|------|------|--------|-------------------|
| `src/Jaminator/Generated/BuildSecrets.g.cs` | generated code | File is `.gitignore`'d by design; never in tree | RESEARCH.md §"Pattern: Generated BuildSecrets.g.cs" (verbatim template) |
| `installer/secrets/README.md` | operator runbook | No operator-runbook README exists in the repo | CONTEXT.md `<specifics>` lines 213-215 (outline) + style match against `docs/manifest-schema.md` |

Both files are net-new content classes. The planner should consume the RESEARCH.md template for `BuildSecrets.g.cs` literally and draft `README.md` from the outline above.

---

## Metadata

**Analog search scope:**
- `src/Jaminator/Models/Manifest.cs` — full file (121 lines, single Read).
- `src/Jaminator/Services/ManifestFetcher.cs` — full file (69 lines, single Read).
- `src/Jaminator/Program.cs` — full file (70 lines, single Read).
- `src/Jaminator/Services/Logger.cs` — full file (40 lines, single Read).
- `src/Jaminator/Jaminator.csproj` — full file (38 lines, single Read).
- `installer/build.ps1` — full file (55 lines, single Read).
- `.gitignore` — full file (30 lines, single Read).
- `.planning/phases/02-private-secrets-channel-manifest-schema/02-CONTEXT.md` — full file (255 lines, single Read).
- `.planning/phases/02-private-secrets-channel-manifest-schema/02-RESEARCH.md` — targeted sections via offset/limit (`Code Examples` lines 521-790, `Architecture Patterns 1–4` lines 226-425); file is 924 lines.

**Files scanned:** 9 source/config + 2 planning artifacts = 11 reads.

**Pattern extraction date:** 2026-05-11.
