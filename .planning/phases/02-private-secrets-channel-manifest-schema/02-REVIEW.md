---
status: resolved
phase: 02
depth: standard
reviewed: 2026-05-31
files_reviewed: 5
files_reviewed_list:
  - src/Jaminator/Models/Manifest.cs
  - installer/build.ps1
  - src/Jaminator/Services/ManifestFetcher.cs
  - src/Jaminator/Program.cs
  - src/Jaminator/UI/MainForm.cs
findings:
  critical: 1
  warning: 3
  info: 2
  total: 6
resolution:
  fixed_in: 7570dbe
  fixed: [CR-01, WR-01, WR-02, WR-03, IN-01]
  not_fixed: [IN-02]
  not_fixed_rationale: "IN-02 (side-effecting cache-path getters) is a pre-existing quality pattern with no correctness impact; left unchanged to avoid churning code outside this phase's scope."
---

> **Resolution (2026-05-31, commit `7570dbe`):** CR-01, WR-01, WR-02, WR-03, and
> IN-01 were fixed during phase execution and verified with a clean `dotnet build`
> (0/0) against a stub containing a backslash + quote, plus a `powershell.exe`
> round-trip check of the verbatim-string escaping. IN-02 was intentionally left
> as-is (pre-existing pattern, no correctness impact). Original findings below.

# Phase 02: Code Review Report

**Reviewed:** 2026-05-31
**Depth:** standard
**Files Reviewed:** 5
**Status:** issues_found

## Summary

Phase 2 implements a dual-fetch Wi-Fi secrets channel: public manifest from GitHub raw content, private SSID→PSK map from the GitHub REST API with PAT bearer auth, joined in memory and cached atomically as a pair. The implementation is structurally sound and honours most of the locked decisions (D-01 through D-16). The security-critical path (per-request bearer auth, SSID-only LINQ projection for log masking, atomic-pair cache write) is correctly implemented.

One Critical finding: the C# source generation in `build.ps1` only escapes double-quotes but not backslashes, which means a secrets URL or PAT value containing a backslash would generate syntactically invalid C# (compiler error) or incorrect string values (silent data corruption depending on the escape sequence). One Warning concerns D-14 being half-implemented — the public manifest fetch has no User-Agent header despite the decision requiring it on both requests. Two further Warnings cover the missing empty-string validation gate in `build.ps1` and a residual `.tmp` file that retains plaintext PSK data on disk after a partial-write failure.

---

## Critical Issues

### CR-01: Incomplete C# string escaping in `build.ps1` — backslash not escaped

**File:** `installer/build.ps1:53-54`

**Issue:** The here-string generator only escapes double-quotes:
```powershell
$escapedPat = $wifiPat -replace '"', '\"'
$escapedUrl = $secretsUrl -replace '"', '\"'
```
A backslash in either value is written verbatim into a C# `const string` literal. In C#, `\` inside a double-quoted string introduces an escape sequence — `\n` becomes a newline, `\t` a tab, `\u` triggers a Unicode code-point parse, etc. If the operator places a URL containing `\` (e.g., a Windows-style path typed into `wifi-secrets-url.txt`) or a PAT with a backslash, one of two outcomes occurs:
- If the resulting sequence is a valid C# escape (`\n`, `\t`, `\\`, etc.) the string is silently wrong at runtime — the EXE embeds a different string than the operator intended.
- If the resulting sequence is invalid (`\j`, `\q`, etc.) `dotnet build` fails with a compiler error with no actionable message pointing to `build.ps1`.

GitHub fine-grained PATs (`github_pat_…`) do not contain backslashes, and HTTPS URLs normally use forward slashes, so the risk is low in practice. But the generator makes no structural guarantee; a defensive operator could introduce `\` in the URL (e.g., by copying a Windows filesystem path), and the build would silently produce a corrupt EXE.

**Fix:** Escape backslashes first, then double-quotes:
```powershell
$escapedPat = $wifiPat  -replace '\\', '\\\\' -replace '"', '\"'
$escapedUrl = $secretsUrl -replace '\\', '\\\\' -replace '"', '\"'
```
Alternatively, use a C# verbatim string literal in the template (`@"..."`) which requires only `""` for embedded double-quotes and treats backslash literally:
```powershell
$escapedPat = $wifiPat  -replace '"', '""'
$escapedUrl = $secretsUrl -replace '"', '""'

$content = @"
...
        internal const string WifiPat = @"$escapedPat";
        internal const string SecretsUrl = @"$escapedUrl";
...
"@
```
The verbatim-string approach is simpler and handles all character classes except embedded double-quotes.

---

## Warnings

### WR-01: D-14 unimplemented for the public manifest fetch — no User-Agent header

**File:** `src/Jaminator/Services/ManifestFetcher.cs:62`

**Issue:** Decision D-14 states: *"Send the standard `User-Agent: Jaminator/<version>` header on both requests."* The private fetch (`FetchSecretsWithBearerAsync`) correctly sets `req.Headers.UserAgent.ParseAdd(userAgent)`. The public fetch uses `Http.GetStringAsync(url + bust)`, which sends no User-Agent header because the shared static `HttpClient` has none configured at construction time. The plan SUMMARY for 02-03 acknowledges this gap but defers it as "out of scope for this plan." GitHub's documentation states that the REST API requires a User-Agent and may reject requests without one; while `raw.githubusercontent.com` (used for the public manifest) is more permissive today, this is an undocumented behavioural dependency.

**Fix:** Set a default User-Agent on the shared client at construction time so both fetches send it automatically:
```csharp
private static readonly HttpClient Http = new HttpClient
{
    Timeout = TimeSpan.FromSeconds(15),
    DefaultRequestHeaders = { { "User-Agent", $"Jaminator/{Program.ToolVersion}" } }
};
```
Or lift the public fetch to `SendAsync` with an explicit `HttpRequestMessage` matching the pattern already used in `FetchSecretsWithBearerAsync`. Note: setting `DefaultRequestHeaders` on a shared client is safe here because the PAT is NOT placed there (it uses per-request headers per D-13).

---

### WR-02: `build.ps1` does not validate that the resolved PAT/URL is non-empty

**File:** `installer/build.ps1:31-45`

**Issue:** If `wifi-pat.txt` exists on disk but contains only whitespace (e.g., a blank file created as a placeholder), `.Trim()` returns `""`. `build.ps1` does not check for this: `$wifiPat` is set to `""`, the condition `if (Test-Path $patFile)` is satisfied, the fallback branch is skipped, and `BuildSecrets.g.cs` is generated with `WifiPat = ""`. The EXE is built and deployed, then fails at first launch with the Program.cs guard (which does check `IsNullOrEmpty`). The failure is caught at runtime rather than build time, causing a confusing operator experience where the build appears to succeed.

The same gap applies to `wifi-secrets-url.txt`.

**Fix:** Add a non-empty assertion after each `.Trim()`:
```powershell
if (Test-Path $patFile) {
    $wifiPat = (Get-Content -Raw $patFile).Trim()
    if ([string]::IsNullOrEmpty($wifiPat)) {
        throw "installer/secrets/wifi-pat.txt exists but is empty — place a valid PAT inside it."
    }
} elseif ($env:JAMINATOR_WIFI_PAT) {
    $wifiPat = $env:JAMINATOR_WIFI_PAT.Trim()
    if ([string]::IsNullOrEmpty($wifiPat)) {
        throw "`$env:JAMINATOR_WIFI_PAT is set but empty."
    }
} else {
    throw "..."
}
```
Apply the same pattern for `$secretsUrl`.

---

### WR-03: `.tmp` files with plaintext PSK data are not deleted on partial-write failure

**File:** `src/Jaminator/Services/ManifestFetcher.cs:165-179`

**Issue:** `WriteCachedPair` writes two `.tmp` files first and then moves them into place. The outer call site swallows all exceptions:
```csharp
try { WriteCachedPair(publicJson, secretsJson); } catch { /* cache write best-effort */ }
```
If `WriteCachedPair` throws after writing `secretsTmp` (which contains the raw plaintext SSID→PSK map) but before completing both `File.Move` calls — for example, due to disk-full or an ACL denial on `File.Delete` — the `.tmp` file at `%ProgramData%\Jaminator\cache\secrets.json.tmp` remains on disk indefinitely. Subsequent successful runs overwrite it with `File.WriteAllText(secretsTmp, secretsJson)`, but if the next run also fails before reaching that line, the file persists. `ProgramData` is readable by all local user accounts.

This is distinct from the accepted T-04 threat (the final `secrets.json` being world-readable), because the `.tmp` file has no naming convention that operators or monitoring tools would associate with sensitive data, and it is never explicitly cleaned up.

**Fix:** Wrap the `.tmp` writes and moves in a cleanup block:
```csharp
private static void WriteCachedPair(string publicJson, string secretsJson)
{
    var manifestPath = CachePath;
    var secretsPath  = SecretsCachePath;
    var manifestTmp  = manifestPath + ".tmp";
    var secretsTmp   = secretsPath  + ".tmp";
    try
    {
        File.WriteAllText(manifestTmp, publicJson);
        File.WriteAllText(secretsTmp,  secretsJson);
        if (File.Exists(manifestPath)) File.Delete(manifestPath);
        File.Move(manifestTmp, manifestPath);
        manifestTmp = null; // transferred — don't delete in finally
        if (File.Exists(secretsPath))  File.Delete(secretsPath);
        File.Move(secretsTmp, secretsPath);
        secretsTmp = null;
    }
    finally
    {
        if (manifestTmp != null && File.Exists(manifestTmp)) try { File.Delete(manifestTmp); } catch { }
        if (secretsTmp  != null && File.Exists(secretsTmp))  try { File.Delete(secretsTmp);  } catch { }
    }
}
```

---

## Info

### IN-01: Fail-fast guard checks `WifiPat` but not `SecretsUrl`

**File:** `src/Jaminator/Program.cs:36-49`

**Issue:** The fail-fast guard at startup checks `BuildSecrets.WifiPat` for empty/placeholder values but does not check `BuildSecrets.SecretsUrl`. If an operator manually creates `BuildSecrets.g.cs` with a real PAT but an empty or placeholder `SecretsUrl`, the guard passes, the application starts normally, and only fails when `ManifestFetcher.FetchAsync` is first called — at which point the error message says "Network fetch failed" rather than "SecretsUrl is not configured." The build-time guard in `build.ps1` prevents this in normal operation (it fails-fast if the URL file is absent), but the runtime guard is incomplete for the same class of manual-stub scenario that the PAT guard defends against.

**Fix:** Extend the guard to cover both symbols:
```csharp
if (string.IsNullOrEmpty(BuildSecrets.WifiPat)   || BuildSecrets.WifiPat   == "@@PAT@@"
 || string.IsNullOrEmpty(BuildSecrets.SecretsUrl) || BuildSecrets.SecretsUrl == "@@URL@@")
{
    var msg = BuildSecrets.SecretsUrl is "" or "@@URL@@"
        ? "Jaminator build is missing the Wi-Fi secrets URL ..."
        : "Jaminator build is missing the Wi-Fi PAT ...";
    // ... existing breadcrumb + return 1 logic
}
```

---

### IN-02: Side-effecting property getters called multiple times per cache operation

**File:** `src/Jaminator/Services/ManifestFetcher.cs:27-49, 165-168`

**Issue:** `CachePath` and `SecretsCachePath` are properties that call `Directory.CreateDirectory(dir)` as a side effect on every access. `WriteCachedPair` accesses each property once (assigning to a local variable — correct). However, `FetchAsync`'s catch block accesses `CachePath` and `SecretsCachePath` twice each: once in the `File.Exists()` guard and once in the `File.ReadAllText()` call. Each access re-creates the directory if it was deleted between the two calls, and performs two `Path.Combine` + `GetFolderPath` evaluations unnecessarily. This is a pre-existing pattern (the `CachePath` getter predates Phase 2), but the new `SecretsCachePath` duplicates the same pattern, making the surface twice as large.

**Fix:** For future hardening: snapshot the paths at the top of `FetchAsync` rather than relying on the property getters. No immediate action required — this is a code quality observation with no correctness impact today.

---

_Reviewed: 2026-05-31_
_Reviewer: Claude (gsd-code-reviewer)_
_Depth: standard_
