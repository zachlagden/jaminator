# Phase 2: Private secrets channel + manifest schema - Research

**Researched:** 2026-05-11
**Domain:** GitHub fine-grained PAT auth, MSBuild generated-file injection, atomic dual-file cache writes, Newtonsoft.Json dictionary deserialisation
**Confidence:** HIGH

## Summary

Phase 2 stands up the private GitHub secrets channel that delivers Wi-Fi PSKs to fleet laptops, extends the `Manifest` DTO with `WifiEntry` / `WifiProfileEntry`, dual-fetches public + private payloads in `ManifestFetcher`, and bakes a fine-grained PAT into the EXE at build time via a generated `BuildSecrets.g.cs`. CONTEXT.md has already locked 16 of the 17 implementation decisions (D-01 through D-16); this research closes the remaining technical unknowns the planner needs to convert those decisions into concrete tasks.

The single most consequential research finding is on auth endpoint shape: **`raw.githubusercontent.com` does NOT support Bearer token authentication for private repos.** It silently ignores `Authorization` headers; a 200 response from a private path is impossible to obtain via raw URLs even with a valid PAT, and a "successful" 200 indicates Git LFS or a public-fork accident, not auth. The canonical path is the **GitHub REST API contents endpoint** (`GET https://api.github.com/repos/{owner}/{repo}/contents/{path}?ref={branch}`) with `Accept: application/vnd.github.raw+json` (or `application/vnd.github.raw`), which returns the raw file bytes (not base64-wrapped JSON) and properly respects the Bearer token. The five other open questions all resolve to single, well-supported patterns in the .NET 4.8 + Microsoft.NET.Sdk + Newtonsoft.Json 13.0.3 toolchain.

**Primary recommendation:** Build URL is `https://api.github.com/repos/jamcoding-internal/jaminator-secrets/contents/secrets.json?ref=main` (or whatever branch/path the operator chooses). Bearer auth attached per-request via `HttpRequestMessage.Headers.Authorization`. Accept header is `application/vnd.github.raw+json`. Generated `BuildSecrets.g.cs` written by `installer/build.ps1` before `dotnet build`, picked up by the SDK's default `**/*.cs` glob — no csproj edit needed. Atomic dual-file cache uses two `.tmp` writes followed by two `File.Move` calls; if the second `Move` fails, the first is rolled back by deleting the just-moved public cache (best-effort; the loud-fail path will throw on next launch and the operator re-runs).

## User Constraints (from CONTEXT.md)

### Locked Decisions

**D-01: Private `secrets.json` shape** — Flat JSON object keyed by SSID, value = PSK. No envelope, no `schemaVersion` field, no nested object. Form: `{ "SchoolNet-Year3": "...", "SchoolNet-Staff": "..." }`.

**D-02: `WifiEntry` wrapper** — Add a top-level wrapper `WifiEntry` with a `Profiles: List<WifiProfileEntry>` field, mirroring the existing `Cleanup` / `Wallpaper` / `Schedule` pattern. Surface it on `Manifest` as `[JsonProperty("wifi")] public WifiEntry? Wifi { get; set; }` (nullable).

**D-03: `WifiProfileEntry` fields** — `[JsonProperty]` snake-key bindings matching WIFI-01 verbatim:
- `Ssid: string` → `"ssid"`
- `AuthMode: string` → `"authMode"` (values: `"WPA2PSK"`, `"WPA3PSK"`, `"open"` — string, not enum)
- `Hidden: bool` → `"hidden"` (default `false`)
- `AutoConnect: bool` → `"autoConnect"` (default `true`)
- `Scope: string` → `"scope"` (values: `"all-users"` (default), `"current-user"`)
- `Psk: string?` → `"psk"` (nullable; **never set from the public manifest**; populated in memory at join time from `secrets.json`)

**D-04: PAT resolution precedence** — `installer/build.ps1` resolves the PAT in this order: (1) `installer/secrets/wifi-pat.txt` if present, (2) `$env:JAMINATOR_WIFI_PAT` if file absent, (3) fail-fast with a clear message.

**D-05: Gitignored secrets directory** — Add `installer/secrets/` to `.gitignore`. Add `installer/secrets/.keep` (empty, force-added) and `installer/secrets/README.md` (committed) explaining PAT placement and rotation.

**D-06: PAT injection via generated `BuildSecrets.g.cs`** — `installer/build.ps1` generates `src/Jaminator/Generated/BuildSecrets.g.cs` before `dotnet build` runs, with `internal const string WifiPat = "@@PAT@@"` and `internal const string SecretsUrl = "@@URL@@"`. `Generated/` is `.gitignore`d.

**D-07: `internal const` accessors** — No runtime extension point for swapping PAT/URL. Threat model is operational opacity, not cryptographic protection.

**D-08: Private repo URL baked into EXE** — `BuildSecrets.SecretsUrl` is build-time-injected from `installer/secrets/wifi-secrets-url.txt` (gitignored) or `$env:JAMINATOR_WIFI_SECRETS_URL`. Not in source, not in public manifest.

**D-09: Sequential fetch order** — Public-first, then private. No parallel `Task.WhenAll`.

**D-10: Both fetches succeeding** — In-memory join `wifi.profiles[i].psk = secrets[wifi.profiles[i].ssid]` for every profile whose SSID has a `secrets.json` entry; profiles with no matching SSID get `Psk = null` and log an `Info`-level skip note. Cache both files atomically as a pair.

**D-11: Either online fetch failing** — Fall back to joined cache pair. If both cached files exist, deserialise and join. If either is missing or corrupt, throw `InvalidOperationException`. No half-state.

**D-12: `FetchAsync` return type** — Existing `(Manifest manifest, bool fromCache)` stays. `fromCache: true` means both files were served from cache.

**D-13: Bearer header per-request** — Reuse the existing static `HttpClient Http`. Attach `Authorization: Bearer <BuildSecrets.WifiPat>` per-request via `HttpRequestMessage`. No second client, no `DefaultRequestHeaders`.

**D-14: User-Agent header** — Send `User-Agent: Jaminator/<version>` on both requests. Version from `Program.ToolVersion`.

**D-15: PSK masking in logs** — Replace `WifiProfileEntry.Psk` with the literal string `***` anywhere it's logged. Pre-emptively override `WifiProfileEntry.ToString()` for Phase 3 safety.

**D-16: Atomic commit strategy** — Five independently-buildable commits in order: DTOs → secrets dir + PAT resolution → fetcher dual-fetch → docs → debug log line.

### Claude's Discretion

- Exact MSBuild wiring for picking up `Generated/BuildSecrets.g.cs` — auto-glob vs explicit `<Compile Include>`. Planner picks after inspecting `Jaminator.csproj`. **Research finding: auto-glob is sufficient. See `## Architecture Patterns / Generated-file injection`.**
- Whether `installer/build.ps1` writes the generated file before the `dotnet build` invocation or whether an MSBuild pre-build target consumes the PAT from env. **Research finding: PS-side write before `dotnet build` is simplest and matches existing `build.ps1` flow. See `## Architecture Patterns / Generated-file injection`.**
- Joined-cache atomic-write technique: single combined-JSON envelope vs two `.tmp` + `Move` operations. **Research finding: two-file `.tmp + Move` is the better fit. See `## Architecture Patterns / Atomic dual-file cache writes`.**
- `JsonConvert.DeserializeObject<Dictionary<string, string>>(secretsJson)` vs typed `Secrets` wrapper class. **Research finding: flat Dictionary is simpler, matches D-01, and Newtonsoft 13.0.3 handles `{ "key": "value", ... }` root objects correctly. See `## Code Examples / Deserialising the SSID→PSK map`.**

### Deferred Ideas (OUT OF SCOPE)

**For Phase 3 (WifiProfileRunner + run-path integration):**
- `WifiProfileRunner` service consuming `WifiProfileEntry` and invoking `netsh wlan add profile filename=<xml> user=<scope>`.
- "wifi" `SectionPanel` in MainForm and adding `"wifi"` to `LoginSafeSections`.
- Skip-with-warning behaviour for profiles where `Psk == null` after join.
- `WifiProfileEntry.ToString()` override.

**For Phase 4 (idempotency, failure isolation, end-to-end smoke):**
- `netsh wlan show profile name=<SSID> key=clear` diff-and-skip check.
- Per-Wi-Fi-failure `Jaminator-wifi-error-*.log` in `%TEMP%`.
- End-to-end smoke test with PSK rotation cycle.

**For Phase 5 (tag and ship v0.8.0):**
- Bumping `Program.ToolVersion` to `"0.8.0"`.
- Production PAT placement, production MSI build, git tag, GitHub Release.

**For future milestones (M3 hardening):**
- HARDEN-02 schema-version validation; HARDEN-06 parallel logon-path I/O; HARDEN-07 CI workflow for automated PAT rotation; HARDEN-01 code-signing; persisted single-file cache envelope.

## Phase Requirements

| ID | Description | Research Support |
|----|-------------|------------------|
| WIFI-01 | Manifest schema additions — `wifi.profiles[]` array carrying SSID, auth mode, hidden flag, auto-connect flag, scope, and (in private manifest only) PSK. Documented in `docs/manifest-schema.md`. | D-02 / D-03 lock the DTO shape; existing `Manifest.cs` patterns (`[JsonProperty]`, sealed classes, `List<T> = new()`) extend cleanly. Newtonsoft 13.0.3 handles nested DTOs without ceremony. See `## Code Examples / WifiEntry + WifiProfileEntry DTOs`. |
| WIFI-03 | Private GitHub manifest repo + fine-grained read-only PAT bundled in MSI. `ManifestFetcher` fetches private secrets in addition to public manifest, joins SSID→PSK in memory at runtime. | Critical finding: must use **GitHub API contents endpoint** (`api.github.com/repos/.../contents/...`) with `Accept: application/vnd.github.raw+json` — raw.githubusercontent.com does NOT support Bearer auth. PAT scope: `Contents: Read` (+ auto-included `Metadata: Read`). Per-request `HttpRequestMessage` for the bearer header. See `## Architecture Patterns / GitHub private-content fetch`. |

## Project Constraints (from CLAUDE.md)

| Constraint | Source | How it shapes Phase 2 |
|------------|--------|----------------------|
| Tech stack locked: .NET 4.8 / C# / WinForms / WiX 4 / Newtonsoft.Json 13.0.3 | CLAUDE.md, STACK.md | No library swaps; extend existing patterns in `Manifest.cs` and `ManifestFetcher.cs`. |
| Build environment: .NET SDK 8+ and WiX 4 CLI on Windows | CLAUDE.md, STATE.md | `BuildSecrets.g.cs` is generated by `installer/build.ps1` (Windows PowerShell, the existing build entry point). Code changes themselves can happen in WSL; the actual signed MSI build with embedded PAT must run on Windows. |
| Sealed DTO classes, `[JsonProperty("snake_case")]` binding, nullable reference types | CONVENTIONS.md, Manifest.cs | `WifiEntry` and `WifiProfileEntry` follow this verbatim. |
| Fail-open with logged context; broad `try/catch` blocks acceptable for non-critical paths | CONVENTIONS.md | The dual-fetch fail-fast path (D-11) is deliberately the **opposite** policy — fatal on private-fetch failure with cache fallback. Document this exception to the codebase norm in the new fetcher code. |
| Single-source-of-truth versioning via `Program.ToolVersion` parsed by `installer/build.ps1` | Phase 1 D-10, build.ps1 regex | Do NOT bump version in Phase 2. Production PAT injection and version bump are Phase 5. |
| Atomic per-file-area commits, each independently buildable | Phase 1 D-11, this phase D-16 | Five commits in the locked order. After commit 4 (`docs`), no compilable code has changed. After commit 5 (the debug log), the binary's behaviour changes but build still works. |
| No automated test suite; verification is manual smoke on Win11 dev box | CLAUDE.md, STATE.md | Phase 2 verification is the dev-laptop debug log line showing joined SSID list with masked PSKs (Success Criterion 5). |
| Manifest never carries secrets in source control | ARCHITECTURE.md anti-pattern "Storing Secrets in Manifest" | Reinforces the locked WIFI-03 design — public manifest stays PSK-free; private repo gets the PSK delivery. |

## Architectural Responsibility Map

| Capability | Primary Tier | Secondary Tier | Rationale |
|------------|-------------|----------------|-----------|
| Wi-Fi profile schema (DTO) | Data Model (`Models/`) | — | All manifest schema lives in `Manifest.cs`. No exception for Wi-Fi. |
| Public manifest fetch | Service (`ManifestFetcher`) | — | Existing responsibility; extended, not replaced. |
| Private secrets fetch + bearer auth | Service (`ManifestFetcher`) | — | Pairs with public fetch; same caching and offline-fallback semantics. New method paired with existing single-URL fetch. |
| In-memory SSID→PSK join | Service (`ManifestFetcher`) | — | Service-layer concern: producer of the joined `Manifest` DTO that the UI/orchestration layer consumes. Join logic does NOT belong in `WifiProfileEntry` constructor or `MainForm`. |
| Atomic two-file cache write | Service (`ManifestFetcher`) | — | Extends existing best-effort cache write at `ManifestFetcher.cs:44`. New pattern introduced here (`.tmp + Move` per file) but scoped to this service. |
| Build-time PAT injection | Build script (`installer/build.ps1`) | csproj (passive: SDK auto-glob picks up `Generated/`) | PowerShell-side generation is simplest; no MSBuild target needed for the recommended approach. |
| Build-time PAT resolution (file → env → fail) | Build script (`installer/build.ps1`) | — | Pure PS logic; no .NET code involved. |
| Public-manifest schema docs | Documentation (`docs/manifest-schema.md`) | — | Existing doc; extended with new `wifi.profiles[]` section. |
| Private-secrets schema docs + operator workflow | Documentation (`docs/manifest-schema.md` + `installer/secrets/README.md`) | — | Two audiences: schema in `docs/manifest-schema.md` (developer-facing); operator workflow in `installer/secrets/README.md` (build-operator-facing). |
| Debug-log emission of joined SSID list | Entry point (`Program.cs`) | Logger | Per CONTEXT.md `<canonical_refs>` block — debug line is emitted immediately after `ManifestFetcher.FetchAsync` returns in `Program.Main()`, NOT inside the fetcher (separation of fetch vs observe). |
| PSK masking on log emission | DTO override (`WifiProfileEntry.ToString()`) + emission sites | — | Pre-emptive mask in `ToString()` (forward-looking for Phase 3); explicit `***` literal at the Phase 2 emission site. |

## Standard Stack

### Core

| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| `Newtonsoft.Json` | 13.0.3 [VERIFIED: src/Jaminator/Jaminator.csproj line 35] | JSON deserialisation for both `Manifest` and `Dictionary<string, string>` secrets | Already in use across the codebase; the project's JSON convention. 13.0.4 exists on NuGet [VERIFIED: nuget.org search 2026-05-11] but no Phase 2 reason to upgrade. |
| `System.Net.Http` | .NET 4.8 BCL | HTTP fetch for both public manifest and private secrets | Already used in `ManifestFetcher.cs:3`. Static `HttpClient` instance already configured. |
| `Microsoft.NET.Sdk` | bundled with .NET SDK 8+ | Default `**/*.cs` Compile glob picks up the generated file in `Generated/` automatically | [CITED: https://learn.microsoft.com/en-us/dotnet/core/project-sdk/overview] confirms default Include glob is `**/*.cs`, default exclude glob does NOT exclude `Generated/`, and `./obj` is excluded via `DefaultItemExcludes`. So a file at `src/Jaminator/Generated/BuildSecrets.g.cs` is auto-included by the existing csproj. |

### Supporting

| Library | Version | Purpose | When to Use |
|---------|---------|---------|-------------|
| `System.Net.Http.Headers.AuthenticationHeaderValue` | .NET 4.8 BCL | Construct the `Authorization: Bearer <token>` header per-request | On every `HttpRequestMessage` for the private fetch. |

### Alternatives Considered

| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| GitHub REST API contents endpoint (`api.github.com/.../contents/...`) | `raw.githubusercontent.com/...` with token in URL or Authorization header | **Rejected — does not work.** `raw.githubusercontent.com` silently ignores Authorization headers for private repos. [CITED: https://github.com/orgs/community/discussions/160828] — community confirmation: "you can throw your personal access token in the header, but it's ignored - no rate limit headers, no feedback, nothing." 401/404 responses are indistinguishable from "auth not honoured." This blocked path is the single most important research finding for this phase. |
| Two-file `.tmp + Move` atomic write | Single combined `cache-bundle.json` envelope (`{ "manifest": ..., "secrets": ... }`) | Envelope is simpler atomically (one write, one move) but it (a) diverges from the existing `manifest.json` cache file layout, (b) loses the ability for a future tool/operator to inspect either file in isolation, (c) ties the cache schema to a Jaminator-internal envelope that needs its own versioning. Two-file approach matches the existing pattern and is deferred-friendly (HARDEN-06's parallel I/O work can reconsider envelope at that point). CONTEXT.md `<decisions> Claude's Discretion` already biases this way. |
| Auto-glob for `Generated/BuildSecrets.g.cs` | Explicit `<Compile Include="Generated/**/*.cs" />` | Auto-glob works (the default Microsoft.NET.Sdk Compile glob is `**/*.cs`). Adding explicit `<Compile Include>` would trigger NETSDK1022 "Duplicate items" error per [CITED: https://learn.microsoft.com/en-us/dotnet/core/tools/sdk-errors/netsdk1022]. **Do NOT add the explicit ItemGroup.** Just write the file to `src/Jaminator/Generated/` and the SDK picks it up. |
| Build-time generation in PowerShell before `dotnet build` | MSBuild `<Target Name="GenerateBuildSecrets" BeforeTargets="BeforeCompile;CoreCompile">` reading the PAT from `$env:JAMINATOR_WIFI_PAT` | PS-side is simpler and obviates the timing risk where MSBuild evaluates the project before the target runs. The MSBuild approach also requires extra plumbing to expose the PAT to the target ([CITED: https://gist.github.com/KirillOsenkov/f20cb84d37a89b01db63f8aafe03f19b] — generated files have evaluation-phase timing issues unless you use `BeforeTargets="BeforeCompile;CoreCompile"`). One caveat: if a developer opens the project in Visual Studio or runs `dotnet build` directly (without going through `build.ps1`), the generated file may be **stale or missing**. The planner should add a stub `BuildSecrets.g.cs` at first generation with empty-string placeholders that produces a clear runtime "PAT not embedded — rebuild via installer/build.ps1" error, OR add a pre-build PowerShell event in the csproj as a backstop. Recommend the runtime fail-fast approach for simplicity. |
| `JsonConvert.DeserializeObject<Dictionary<string, string>>` | Typed `Secrets` wrapper class with `[JsonExtensionData] Dictionary<string, object> Items` | Flat `Dictionary<string, string>` is simpler, matches D-01 verbatim, and Newtonsoft 13.0.3 deserialises a JSON root object directly into a Dictionary without any special configuration [CITED: https://www.newtonsoft.com/json/help/html/DeserializeDictionary.htm]. |
| `Authorization: Bearer <token>` | `Authorization: token <token>` | Both work for GitHub PATs per [CITED: https://docs.github.com/en/rest/authentication/authenticating-to-the-rest-api] ("In most cases, you can use `Authorization: Bearer` or `Authorization: token` to pass a token"). Bearer is documented as the primary form in current GitHub docs. Use Bearer for consistency with current GitHub examples. |
| `internal const string WifiPat` | `[assembly: AssemblyMetadata("WifiPat", "...")]` attribute | `internal const` is inlined into the call-site IL at compile time — no reflection at runtime. AssemblyMetadata requires `Assembly.GetCustomAttribute<>()` at every call. CONTEXT.md D-06 already locks this; research confirms no operational reason to revisit. |

**Installation:**

No new package references required. Existing `Newtonsoft.Json` 13.0.3 and `System.Net.Http` BCL reference suffice.

**Version verification:**

- `Newtonsoft.Json` 13.0.3 — present at `src/Jaminator/Jaminator.csproj` line 35. Latest stable is 13.0.4 [VERIFIED: nuget.org 2026-05-11], no Phase 2 reason to upgrade.
- `Microsoft.NETFramework.ReferenceAssemblies.net48` 1.0.3 — present at `src/Jaminator/Jaminator.csproj` line 32. No change needed.

## Architecture Patterns

### System Architecture Diagram

```text
                                 BUILD TIME (Windows)
                                       │
   installer/secrets/wifi-pat.txt ─┐   │
   $env:JAMINATOR_WIFI_PAT ─────────┤  │
                                    ▼  ▼
                            installer/build.ps1
                                  resolves PAT ──┐
                                  resolves URL ──┤
                                                  ▼
                              writes (overwriting on each build):
                              src/Jaminator/Generated/BuildSecrets.g.cs
                                  internal const string WifiPat = "..."
                                  internal const string SecretsUrl = "..."
                                                  │
                                                  ▼
                              dotnet build (Microsoft.NET.Sdk auto-glob
                              picks up Generated/*.cs)
                                                  │
                                                  ▼
                              wix build → Jaminator.msi  (PAT inlined into IL)


                                 RUN TIME (every laptop)
                                       │
                          Program.Main → Mode dispatch
                                       │
                                       ▼
                          ManifestFetcher.FetchAsync(publicUrl)
                                       │
              ┌─────── public fetch ──┴─────── private fetch ─────────┐
              ▼                                                          ▼
   GET raw.githubusercontent.com/                          GET api.github.com/repos/
   zachlagden/jaminator/main/                              <org>/<priv-repo>/contents/
   manifest/manifest.json?t=<unix>                         secrets.json?ref=main&t=<unix>
   (no auth — public)                                      Authorization: Bearer <PAT>
                                                            Accept: application/vnd.github.raw+json
                                                            User-Agent: Jaminator/<version>
              │                                                          │
              ▼                                                          ▼
   Manifest JSON                                          { "SSID-A": "PSK-A", ... }
              │                                                          │
              └────────────────────┬────────────────────────────────────┘
                                   ▼
                         Join SSID→PSK in memory
                         (wifi.profiles[i].psk = secrets[ssid])
                                   │
                                   ▼
                         Atomic write pair to cache:
                         %ProgramData%\Jaminator\cache\manifest.json.tmp → manifest.json
                         %ProgramData%\Jaminator\cache\secrets.json.tmp  → secrets.json
                                   │
                                   ▼
                         return (Manifest, fromCache: false)
                                   │
                                   ▼
                   ON EITHER FETCH FAILURE:
                   load joined cache pair (both files must exist & deserialise)
                   throw InvalidOperationException if either is missing/corrupt
                                   │
                                   ▼
                         Program.cs logs:
                         "Joined manifest: 1 Wi-Fi profile(s) — [TestSSID] (PSKs: ***)"
```

### Pattern 1: GitHub private-content fetch via REST API

**What:** Fetch a single file from a private GitHub repo using a fine-grained PAT with Bearer auth.

**When to use:** Any case where you need authenticated access to a private repo's content. This is the canonical pattern; `raw.githubusercontent.com` is NOT a valid alternative.

**Why this matters:** `raw.githubusercontent.com` silently ignores Authorization headers for private repos. A 200 response would mean "I served you something" but with a PAT that lacks repo access, you'd get a 404, with no indication that auth was tried. The community-confirmed answer is to use the REST API contents endpoint. [CITED: https://github.com/orgs/community/discussions/160828], [CITED: https://github.com/orgs/community/discussions/36459]

**URL form:**

```
https://api.github.com/repos/{owner}/{repo}/contents/{path}?ref={branch}
```

For `jamcoding-internal/jaminator-secrets` with `secrets.json` at repo root on `main` branch:

```
https://api.github.com/repos/jamcoding-internal/jaminator-secrets/contents/secrets.json?ref=main
```

**Headers:**

```http
Authorization: Bearer <fine-grained-PAT>
Accept: application/vnd.github.raw+json
User-Agent: Jaminator/<version>
X-GitHub-Api-Version: 2026-03-10
```

Notes:
- `Accept: application/vnd.github.raw+json` returns the **raw file bytes** as the response body, not a base64-wrapped JSON envelope. [CITED: https://docs.github.com/en/rest/repos/contents]
- `Accept: application/vnd.github.raw` (without `+json`) also works and is functionally equivalent for this endpoint.
- `X-GitHub-Api-Version` is optional but recommended for forward-compatibility per [CITED: https://docs.github.com/en/rest/authentication/authenticating-to-the-rest-api]. Pinning the API version means a future GitHub default-bump won't break this code. The Jaminator binary fleet should pin so a rebuild is needed to opt into changes.
- `User-Agent` is mandatory for GitHub API — without one, requests are rejected with 403. Send `Jaminator/<Program.ToolVersion>` to align with the public-fetch precedent.

**PAT scope (minimum):**
- **Contents: Read** on the private repo only [CITED: https://docs.github.com/en/rest/authentication/permissions-required-for-fine-grained-personal-access-tokens]
- **Metadata: Read** is auto-included by GitHub when any other repo permission is granted [VERIFIED: GitHub community discussion 133558]
- No org-level permissions needed. Token must be repo-scoped (only the private secrets repo selected, not "All repositories").

**Rate limits:**
- Authenticated API calls have a 5,000 req/hr per-PAT ceiling [CITED: https://docs.github.com/en/rest/using-the-rest-api/rate-limits-for-the-rest-api].
- A fleet of N laptops fetching once per logon → N requests/day approximately. Even a 500-laptop fleet with multiple-logon-per-day usage is **two orders of magnitude under** the limit. Not a concern for v0.8.0.
- Secondary rate limit caps at 100 concurrent requests, also not a concern for staggered classroom logon-time fetches.

### Pattern 2: Generated-file injection via SDK auto-glob

**What:** Write a `.cs` file to a directory under the project root; the Microsoft.NET.Sdk default Compile glob picks it up automatically. No csproj edits.

**When to use:** Build-time code generation where the file path is deterministic and the SDK is `Microsoft.NET.Sdk` (or a derivative). This is the simplest mechanism. [CITED: https://learn.microsoft.com/en-us/dotnet/core/project-sdk/overview — "Compile: **/*.cs"]

**Required directory:** `src/Jaminator/Generated/BuildSecrets.g.cs`

**Why this works:**
- Default Compile Include glob: `**/*.cs`
- Default Compile Exclude glob: `**/*.user; **/*.*proj; **/*.sln(x); **/*.vssscc`
- `./obj` and `./bin` excluded via `$(BaseOutputPath)` / `$(BaseIntermediateOutputPath)` defaults
- `Generated/` matches `**/*.cs` and does not match any exclude → automatically compiled.

**Anti-pattern to avoid:** Do NOT add `<Compile Include="Generated/**/*.cs" />` to the csproj. This triggers `NETSDK1022: Duplicate items were included` per [CITED: https://learn.microsoft.com/en-us/dotnet/core/tools/sdk-errors/netsdk1022], because the same file is now included twice (once by auto-glob, once by the explicit Include).

**Generation timing — PowerShell-side, before `dotnet build`:**

```powershell
$generatedPath = "$repoRoot\src\Jaminator\Generated\BuildSecrets.g.cs"
$generatedDir = Split-Path $generatedPath
New-Item -ItemType Directory -Path $generatedDir -Force | Out-Null

$content = @"
// Auto-generated by installer/build.ps1 at build time. Do not edit; do not commit.
// Contents are gitignored. Rotation: replace installer/secrets/wifi-pat.txt and rebuild.
namespace Jaminator
{
    internal static class BuildSecrets
    {
        internal const string WifiPat = "$wifiPat";
        internal const string SecretsUrl = "$secretsUrl";
    }
}
"@
Set-Content -Path $generatedPath -Value $content -NoNewline -Encoding UTF8

# Then proceed with the existing dotnet build invocation
dotnet build "$repoRoot\Jaminator.sln" -c $Configuration | Out-Host
```

**Stale-file pitfall:** If a developer runs `dotnet build` directly (bypassing `build.ps1`), `Generated/BuildSecrets.g.cs` may be missing or contain stale placeholder values from a previous run. Mitigation: commit nothing in `Generated/` (it's `.gitignore`d), and have `Program.Main` fail-fast with a clear message if `BuildSecrets.WifiPat` is the empty string OR equals a known placeholder like `"@@PAT@@"`. This makes "PS-bypass" detectable at first launch rather than 1 hour later when the fetch returns 401.

### Pattern 3: Atomic dual-file cache writes

**What:** Two files (`manifest.json` and `secrets.json`) must be cache-written such that a crash mid-operation does not leave them in an inconsistent state (one fresh, one stale, or one written and the other partial).

**When to use:** Any time two files form a logical unit and must be observed together by a future reader. This is the pattern Phase 2 introduces; not yet present elsewhere in the codebase.

**Recommended approach: two `.tmp` files, then two `File.Move` calls in order.**

```csharp
var manifestTmp = manifestPath + ".tmp";
var secretsTmp = secretsPath + ".tmp";

try {
    File.WriteAllText(manifestTmp, manifestJson);
    File.WriteAllText(secretsTmp, secretsJson);
    // Both .tmp files now exist on disk. From here, the worst case is:
    //   - We crash between Move #1 and Move #2 → public cache updated, private cache stale.
    //   - On next launch, the joined cache load will still SUCCEED (both files exist; one
    //     is fresh, one is the previous version) but the join will produce stale PSKs for
    //     any SSID added since the last successful pair. This is a rare and benign degradation
    //     for the v0.8.0 mode; HARDEN-06 can revisit.
    File.Move(manifestTmp, manifestPath, overwrite: true);  // Net Framework 4.8 lacks the overwrite overload
    File.Move(secretsTmp, secretsPath, overwrite: true);    // — see note below
} catch {
    // best-effort cleanup of any .tmp file left behind
    try { File.Delete(manifestTmp); } catch { }
    try { File.Delete(secretsTmp); } catch { }
}
```

**Net Framework 4.8 quirk:** `File.Move(string, string, bool)` overload (with `overwrite: true`) is **.NET Core 3.0+ only**. On .NET 4.8 you must `File.Delete` the destination before `File.Move`, OR use `File.Replace(srcTmp, destination, backup: null)` which IS available on .NET 4.8 and is documented as atomic on Windows via the underlying `ReplaceFile()` API [CITED: https://learn.microsoft.com/en-us/dotnet/api/system.io.file.replace] and confirmed atomic in multiple sources [CITED: https://antonymale.co.uk/windows-atomic-file-writes.html], [CITED: https://github.com/dotnet/runtime/issues/18034].

**Final recommendation for .NET 4.8:**

```csharp
File.Delete(manifestPath);  // OK if file doesn't exist? NO — throws on .NET 4.8 if file doesn't exist? NO,
                            // File.Delete is documented as no-op if file doesn't exist. Safe.
File.Move(manifestTmp, manifestPath);
File.Delete(secretsPath);
File.Move(secretsTmp, secretsPath);
```

OR, using `File.Replace` (requires destination to exist):

```csharp
if (File.Exists(manifestPath)) {
    File.Replace(manifestTmp, manifestPath, destinationBackupFileName: null);
} else {
    File.Move(manifestTmp, manifestPath);  // first-time-cache case
}
// repeat for secrets
```

The `File.Delete + File.Move` pattern is simpler and more readable; both are atomic at the OS level on NTFS for files <= a sector size, but neither is atomic across the **pair**. The two-file consistency window is unavoidable without WAL semantics. Document the rare-failure mode (crash between the two Moves → one stale, one fresh) in the fetcher's code comment and accept it for v0.8.0.

**Why not the envelope?** A single `cache-bundle.json` containing both payloads would be atomic for the pair, but it breaks the existing `manifest.json` cache contract (other code paths or future tools may read it directly), forces a custom schema, and gains us very little vs the two-file approach for a non-cryptographic operational cache. Defer the envelope to HARDEN-06 (parallel I/O redesign).

### Pattern 4: Per-request Bearer header on a shared HttpClient

**What:** Attach `Authorization: Bearer <token>` per-request via `HttpRequestMessage`, NOT via `HttpClient.DefaultRequestHeaders.Authorization`.

**When to use:** When a single `HttpClient` instance is shared across requests with different auth requirements (here: one public fetch with no auth, one private fetch with PAT). Setting `DefaultRequestHeaders` on the shared client would leak the PAT to the public fetch.

**Code:**

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

**.NET 4.8 quirk:** `using` declaration syntax (`using var req = ...` without braces) is C# 8.0+. The project's csproj has `<LangVersion>latest</LangVersion>`, which on .NET SDK 8+ resolves to C# 12 — so this syntax works. If you'd rather keep the codebase pinned to a more conservative C# version, use the explicit `using (var req = ...) { ... }` block syntax — both are functionally identical.

### Recommended Project Structure

```
src/Jaminator/
├── Generated/                       # gitignored; created by build.ps1
│   └── BuildSecrets.g.cs            # internal const string WifiPat, SecretsUrl
├── Models/
│   └── Manifest.cs                  # extend: WifiEntry, WifiProfileEntry, Manifest.Wifi
├── Services/
│   └── ManifestFetcher.cs           # extend: dual-fetch + join + atomic cache pair
└── Program.cs                       # extend: post-fetch debug log line

installer/
├── secrets/                         # NEW directory
│   ├── .keep                        # empty, force-added
│   ├── README.md                    # operator-facing PAT workflow doc
│   ├── wifi-pat.txt                 # gitignored — operator writes
│   └── wifi-secrets-url.txt         # gitignored — operator writes
└── build.ps1                        # extend: PAT/URL resolution + BuildSecrets.g.cs generation

docs/
└── manifest-schema.md               # extend: wifi.profiles[] + secrets.json blocks
```

### Anti-Patterns to Avoid

- **PAT in URL like `https://$TOKEN@raw.githubusercontent.com/...`:** GitHub community consistently flags this as the wrong approach [CITED: https://github.com/orgs/community/discussions/36459]; also exposes the token in process listings and HTTP logs. Use the Authorization header.
- **`HttpClient.DefaultRequestHeaders.Authorization = ...` on the shared static client:** the PAT would leak to the public-manifest fetch. Per-request `HttpRequestMessage` is the only correct shape.
- **Explicit `<Compile Include="Generated/**/*.cs" />` in csproj:** triggers NETSDK1022 because the SDK auto-glob already picks it up.
- **Committing `Generated/BuildSecrets.g.cs`:** even a placeholder version. Always gitignored — there is never a valid reason to commit a generated secret-carrying file, and a committed placeholder confuses git status during builds.
- **Encrypting the PAT at rest:** explicitly rejected by D-07 (operational opacity, not crypto). Adding "obfuscation" would just signal where the secret is.
- **A second `HttpClient` instance for the private fetch:** socket-exhaustion footgun ([CITED: https://learn.microsoft.com/en-us/dotnet/api/system.net.http.httpclient — "intended to be instantiated once and reused"]). Reuse the existing static `Http` client; vary auth per-request.
- **`Console.WriteLine` of the PSK during dev-laptop testing:** even briefly. Use `***` everywhere from the start; rely on the dev-laptop knowing what PSK it set in the private repo.

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| JSON deserialisation of `{ "key": "value", ... }` into a Dictionary | Custom token-by-token parser | `JsonConvert.DeserializeObject<Dictionary<string, string>>(json)` | Newtonsoft 13.0.3 handles this trivially. Custom parsing reinvents BOM handling, escape sequences, etc. |
| Atomic file replacement on Windows | Direct `WriteAllText` over existing file | `File.Delete` + `File.Move`, or `File.Replace(src, dest, backup: null)` for files that already exist | Both delegate to OS atomic APIs (`MoveFileEx`, `ReplaceFile`); rolling your own gets you partial writes on crash. |
| Bearer-token header construction | String-concat `"Bearer " + token` and `.Add("Authorization", ...)` | `new AuthenticationHeaderValue("Bearer", token)` assigned to `req.Headers.Authorization` | The strongly-typed `AuthenticationHeaderValue` does the right thing with whitespace, validates the scheme, and is the documented pattern. |
| GitHub authentication against raw.githubusercontent.com | Try various combinations of `Authorization: token`, `Authorization: Bearer`, URL embedding, `?token=` query param | Use the REST API contents endpoint with `Accept: application/vnd.github.raw+json` | raw.githubusercontent.com does not support authentication. Period. Trying to make it work wastes time and ships a brittle hack. |
| MSBuild target to generate `.cs` file before compile | Custom `<Target Name="..." BeforeTargets="...">` block in csproj reading from env vars | PowerShell `Set-Content` before `dotnet build` is invoked | The PS approach is the simplest mechanism that achieves "regenerate the file every build, never commit it." The MSBuild target approach is only needed if the project is built by VS / dotnet CLI directly without going through `build.ps1`, which Jaminator's pipeline doesn't do for the MSI workflow. |
| gitignore "directory except some files" patterns from scratch | Manually `installer/secrets/wifi-pat.txt` + `installer/secrets/wifi-secrets-url.txt` ignore lines | `installer/secrets/*` + `!installer/secrets/.keep` + `!installer/secrets/README.md` | The directory-then-negate idiom is the documented git pattern [CITED: https://git-scm.com/docs/gitignore]. Whitelisting specific PAT filenames is fragile — anyone who adds a new secret-bearing file will commit it by accident. The negation pattern is the safer default. |

**Key insight:** This phase introduces almost no novel mechanics — every problem has a canonical .NET 4.8 / Newtonsoft / git pattern. The only genuinely tricky finding is the GitHub auth endpoint, where the popular intuition (`raw.githubusercontent.com` + Bearer header) silently fails on private repos.

## Runtime State Inventory

Phase 2 is a **greenfield additive feature**, not a rename or refactor. No existing stored data, OS-registered state, or build artifacts will be invalidated by this work. Specifically:

| Category | Items Found | Action Required |
|----------|-------------|------------------|
| Stored data | None — `%ProgramData%\Jaminator\cache\manifest.json` continues to be written at the same path with the same schema (just gains a new optional top-level `wifi` field in the JSON). The new `%ProgramData%\Jaminator\cache\secrets.json` is created from scratch on first run. | None |
| Live service config | None — no n8n / Datadog / Tailscale / Cloudflare / external service is involved. Pure Windows desktop app. | None |
| OS-registered state | None — no Windows Task Scheduler / pm2 / systemd registrations change. The existing `Jaminator-Login` and `Jaminator-Daily` tasks already point at `Jaminator.exe`; their behaviour on launch changes (they now do a second fetch) but the registration is untouched. | None |
| Secrets and env vars | New: `installer/secrets/wifi-pat.txt`, `installer/secrets/wifi-secrets-url.txt`, `$env:JAMINATOR_WIFI_PAT`, `$env:JAMINATOR_WIFI_SECRETS_URL`. These are all introduced by this phase, gitignored, and operator-managed. No existing secret is affected. | Operator action: place files / set env vars on the Windows build box per `installer/secrets/README.md`. |
| Build artifacts / installed packages | None — existing `bin/`, `obj/`, MSI artifacts continue to work. The next `installer/build.ps1` run regenerates `src/Jaminator/Generated/BuildSecrets.g.cs` from scratch (it doesn't exist before then; the `Generated/` directory is created on first build). | None |

**The canonical question:** *After every file in the repo is updated, what runtime systems still have the old state cached, stored, or registered?* — Answer: nothing. v0.7.4 / v0.7.5 fleet installs continue to operate unchanged until they SelfUpdate to v0.8.0 (Phase 5), at which point they pick up the new behaviour on first launch.

## Common Pitfalls

### Pitfall 1: `raw.githubusercontent.com` for private repo content

**What goes wrong:** Developer attempts `https://raw.githubusercontent.com/<org>/<priv-repo>/main/secrets.json` with `Authorization: Bearer <PAT>` and gets a 404, or worse, gets a 200 from a public fork they didn't realise existed.

**Why it happens:** Intuition says "raw.githubusercontent.com is the file-content host; the API is for metadata." This is true for public files but false for private files — raw simply has no auth surface. Community confirmation [CITED: https://github.com/orgs/community/discussions/160828]: "you can throw your personal access token in the header, but it's ignored."

**How to avoid:** Use the REST API contents endpoint, period. Document this anti-pattern in the inline code comment so the next maintainer doesn't "simplify" the fetch back to the raw URL.

**Warning signs:** 404 from a `raw.githubusercontent.com` private path even with a valid PAT. A debug log entry showing 200 but the response body is suspiciously different from what's in the private repo (could be a stale or public fork).

### Pitfall 2: Generated file missing when developer runs `dotnet build` directly

**What goes wrong:** Developer opens the solution in Visual Studio (or runs `dotnet build` without `build.ps1`). `src/Jaminator/Generated/BuildSecrets.g.cs` doesn't exist (it's gitignored and only created by `build.ps1`). Build fails with `CS0103: The name 'BuildSecrets' does not exist in the current context`.

**Why it happens:** PS-side generation only runs during `installer/build.ps1`. VS and direct `dotnet build` invocations bypass it.

**How to avoid (recommended):** Have `installer/build.ps1` write a placeholder `BuildSecrets.g.cs` that compiles cleanly with **empty-string consts**, NEVER commit it (still gitignored), and have `Program.Main` (or `ManifestFetcher`) fail-fast at startup with a clear message if `BuildSecrets.WifiPat` is empty / equals `"@@PAT@@"` placeholder / equals known placeholder. This means a developer doing `dotnet build` in VS gets a binary that compiles but loudly refuses to run, with a single-line message telling them to run `installer/build.ps1`.

**Alternative:** Add a pre-build Target in csproj that detects missing `Generated/BuildSecrets.g.cs` and writes a placeholder. More complex; not strictly necessary.

**Warning signs:** `CS0103: The name 'BuildSecrets' does not exist` from a Visual Studio build. Or a stale `BuildSecrets.g.cs` from a prior build leaking a previous PAT into a current build.

### Pitfall 3: PSK leak via `WifiProfileEntry.ToString()` or interpolation

**What goes wrong:** A Phase 3 / Phase 4 logger emits `log.Info($"profile: {entry}")` and the default `ToString()` happily prints the PSK. The PSK ends up in `%ProgramData%\Jaminator\logs\jaminator-YYYYMMDD.log` — a file readable by every local admin on the laptop.

**Why it happens:** C# auto-generates `ToString()` returning the type name unless overridden — but if someone adds a `record` style override or a developer hand-writes one without thinking about masking, the PSK is in the property and trivially exposed.

**How to avoid:** Phase 2 D-15 already commits to a pre-emptive `WifiProfileEntry.ToString()` override that masks the PSK. Implement it now even though no caller logs the entry yet — it's a forward-looking safety latch for Phase 3.

**Warning signs:** A search for `WifiProfileEntry` in log output returns more than just SSID names. A bisect on Phase 3 logs reveals a PSK in plaintext.

### Pitfall 4: `File.Move(src, dest, overwrite: true)` not available on .NET 4.8

**What goes wrong:** Developer (or AI assistant) writes `File.Move(tmp, dest, overwrite: true)` thinking it's a standard overload. Build fails on .NET 4.8 with `CS1501: No overload for method 'Move' takes 3 arguments`.

**Why it happens:** The 3-arg overload was added in .NET Core 3.0. .NET Framework 4.8 only has the 2-arg form.

**How to avoid:** Use `File.Delete(dest)` (no-op if absent) followed by `File.Move(tmp, dest)`. Or use `File.Replace(src, dest, backup: null)` if `dest` is known to exist. Document the .NET 4.8 constraint in the fetcher's inline comment so a future port to a newer framework doesn't keep the unnecessary delete.

**Warning signs:** Build error at the cache-write site. CI/local-build mismatch (if a developer's tooling is targeting net6+ accidentally).

### Pitfall 5: PAT or URL leak via verbose-log capture

**What goes wrong:** A Phase 2 verbose MSI log (`msiexec /l*v`) or a Process Monitor capture during install reveals the PAT in some MSI custom action argument or file path.

**Why it happens:** The PAT is baked into the EXE — not into the MSI's properties or custom action argument list. So this **shouldn't** happen for the current design. But if the planner ever introduces a custom action that takes the PAT as a parameter (e.g., to validate it at install time), it would suddenly be exposed.

**How to avoid:** Never pass the PAT through MSI properties or custom action arguments. It stays inside the EXE binary, period. Document this constraint in `installer/secrets/README.md` so future maintainers don't add an "install-time PAT validation step."

**Warning signs:** A new MSI custom action that takes a `WifiPat=` or similar argument.

### Pitfall 6: `Generated/` not auto-globbed because of an unexpected exclude

**What goes wrong:** SDK auto-glob skips `Generated/` because some upstream `Directory.Build.props` or `.editorconfig` exclude pattern catches it.

**Why it happens:** Unlikely in this codebase (no `Directory.Build.props` exists), but the SDK respects `<DefaultItemExcludes>` and per-project Compile-Remove items.

**How to avoid:** Before completing Phase 2, verify by running `dotnet msbuild src/Jaminator/Jaminator.csproj -preprocess:out.xml` and grepping for `Generated/BuildSecrets.g.cs` in the output. If not present, fall back to an explicit `<Compile Include="Generated/BuildSecrets.g.cs" />`. This verification is mechanical and adds one minute to the planner's task list.

**Warning signs:** First Phase 2 build with `BuildSecrets.g.cs` written succeeds but `BuildSecrets.WifiPat` is `null` at runtime → file was generated but not compiled in.

## Code Examples

### Pattern: WifiEntry + WifiProfileEntry DTOs

```csharp
// Source: extends Models/Manifest.cs following the codebase pattern from
// CONVENTIONS.md and the existing CleanupEntry/WallpaperEntry sealed-class
// + [JsonProperty] convention.

public sealed class WifiEntry
{
    /// <summary>Wi-Fi profiles to deploy via netsh wlan add profile. PSKs are
    /// populated in memory at runtime from the private secrets channel; the
    /// public manifest never carries them.</summary>
    [JsonProperty("profiles")] public List<WifiProfileEntry> Profiles { get; set; } = new();
}

public sealed class WifiProfileEntry
{
    [JsonProperty("ssid")] public string Ssid { get; set; } = "";

    /// <summary>"WPA2PSK" (default), "WPA3PSK", or "open". String-typed for
    /// authoring forgiveness; runtime validates at deploy site.</summary>
    [JsonProperty("authMode")] public string AuthMode { get; set; } = "WPA2PSK";

    [JsonProperty("hidden")] public bool Hidden { get; set; }
    [JsonProperty("autoConnect")] public bool AutoConnect { get; set; } = true;

    /// <summary>"all-users" (default) or "current-user".</summary>
    [JsonProperty("scope")] public string Scope { get; set; } = "all-users";

    /// <summary>Pre-shared key. NEVER set from the public manifest — populated
    /// in memory by ManifestFetcher from the private secrets channel.</summary>
    [JsonProperty("psk")] public string? Psk { get; set; }

    /// <summary>Override masks the PSK so accidental log interpolation never
    /// leaks credentials. Pre-emptive Phase 2 safety latch for Phase 3 callers.</summary>
    public override string ToString() =>
        $"WifiProfile(ssid='{Ssid}', authMode={AuthMode}, scope={Scope}, psk={(Psk == null ? "(none)" : "***")})";
}

// Wire into Manifest:
public sealed class Manifest
{
    // ... existing fields ...
    [JsonProperty("wifi")] public WifiEntry? Wifi { get; set; }
}
```

### Pattern: Generated BuildSecrets.g.cs (the file build.ps1 writes)

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

### Pattern: PowerShell PAT resolution + file generation in build.ps1

```powershell
# Add to installer/build.ps1 BEFORE the existing `dotnet build` invocation.

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

# Escape any embedded double quotes (defensive — should be none for a PAT)
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

# --- (existing) dotnet build invocation continues below ---
```

### Pattern: Dual-fetch + join in ManifestFetcher.FetchAsync

```csharp
// Sketch — actual implementation in ManifestFetcher.cs

public async Task<(Manifest manifest, bool fromCache)> FetchAsync(string url)
{
    try
    {
        // Public fetch (existing pattern)
        var bust = $"?t={DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";
        var publicJson = await Http.GetStringAsync(url + bust).ConfigureAwait(false);
        var manifest = JsonConvert.DeserializeObject<Manifest>(publicJson)
            ?? throw new InvalidOperationException("Public manifest deserialised to null");

        // Private fetch (new) — GitHub REST API contents endpoint with bearer auth
        var secretsBust = $"&t={DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";
        var secretsJson = await FetchSecretsWithBearerAsync(
            BuildSecrets.SecretsUrl + secretsBust,
            BuildSecrets.WifiPat,
            $"Jaminator/{Program.ToolVersion}"
        ).ConfigureAwait(false);
        var secrets = JsonConvert.DeserializeObject<Dictionary<string, string>>(secretsJson)
            ?? throw new InvalidOperationException("Private secrets deserialised to null");

        // Join SSID → PSK
        JoinPsks(manifest, secrets);

        // Atomic cache write of the pair
        WriteCachedPair(publicJson, secretsJson);

        return (manifest, fromCache: false);
    }
    catch (Exception netEx)
    {
        // Joined cache fallback
        var manifestCache = CachePath;
        var secretsCache = SecretsCachePath;
        if (File.Exists(manifestCache) && File.Exists(secretsCache))
        {
            try
            {
                var publicJson = File.ReadAllText(manifestCache);
                var secretsJson = File.ReadAllText(secretsCache);
                var manifest = JsonConvert.DeserializeObject<Manifest>(publicJson)
                    ?? throw new InvalidOperationException("Cached public manifest deserialised to null");
                var secrets = JsonConvert.DeserializeObject<Dictionary<string, string>>(secretsJson)
                    ?? throw new InvalidOperationException("Cached private secrets deserialised to null");
                JoinPsks(manifest, secrets);
                return (manifest, fromCache: true);
            }
            catch (Exception cacheEx)
            {
                throw new InvalidOperationException(
                    $"Network fetch failed ({netEx.Message}) and joined cache is corrupt ({cacheEx.Message})", netEx);
            }
        }
        throw new InvalidOperationException(
            $"Network fetch failed and no joined cached pair exists: {netEx.Message}", netEx);
    }
}

private static void JoinPsks(Manifest manifest, Dictionary<string, string> secrets)
{
    if (manifest.Wifi == null) return;
    foreach (var profile in manifest.Wifi.Profiles)
    {
        if (secrets.TryGetValue(profile.Ssid, out var psk))
        {
            profile.Psk = psk;
        }
        // Else: leave Psk as null; Phase 3's runner skips entries with no PSK
        // and logs a clear message. Don't log here — separation of concerns.
    }
}
```

### Pattern: Deserialising the SSID→PSK map

```csharp
// Source: https://www.newtonsoft.com/json/help/html/DeserializeDictionary.htm
// secretsJson example: { "TestSSID": "TestPSK", "SchoolNet-Year3": "..." }

var secrets = JsonConvert.DeserializeObject<Dictionary<string, string>>(secretsJson);
// secrets["TestSSID"] == "TestPSK"
```

**Edge cases verified:**
- BOM in the JSON file: Newtonsoft strips it transparently when deserialising a string [CITED: Newtonsoft 13.x release notes].
- `null` values in the JSON (`{ "SchoolNet": null }`): deserialises to `null` in the Dictionary value. Defensive callers should `if (psk != null)` before assigning, but this is a malformed-private-repo case worth surfacing.
- Case sensitivity: keys are case-sensitive by default. Document this in the operator README — SSIDs in `secrets.json` must exactly match SSIDs in the public manifest. Wi-Fi SSIDs ARE case-sensitive on Windows so this matches platform reality.

### Pattern: gitignore additions

```gitignore
# Add to .gitignore (after the existing rules)

# Local manifest overrides (for testing without committing) -- existing rule above
# (the line that's already present)

# Wi-Fi secrets channel — never commit the PAT or the private secrets URL
installer/secrets/*
!installer/secrets/.keep
!installer/secrets/README.md

# Generated build-time secrets — written by installer/build.ps1
src/Jaminator/Generated/
```

The directory-then-negate pattern is the documented git idiom for "ignore everything in this dir except these specific files" [CITED: https://git-scm.com/docs/gitignore — "An optional prefix "!" negates the pattern"].

Important git limitation [CITED: git-scm docs]: "It is not possible to re-include a file if a parent directory of that file is excluded." This is why we use `installer/secrets/*` (ignore contents) and NOT `installer/secrets/` (ignore the dir entirely). The trailing `/*` is essential.

### Pattern: Debug log emission site

```csharp
// In Program.Main (UI mode) or wherever ManifestFetcher.FetchAsync is called.
// Per CONTEXT.md the emission belongs in the caller, NOT inside the fetcher.

var (manifest, fromCache) = await fetcher.FetchAsync(ManifestUrl).ConfigureAwait(false);

// Debug log line for Success Criterion 5
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

**Caveat on placement:** `Program.Main` doesn't currently instantiate `Logger` for UI mode (it's done inside `MainForm`). The cleanest insertion point may actually be `MainForm.OnLoad()` immediately after the fetch returns, OR within a slim wrapper service that both `Program.Main` (headless modes) and `MainForm` call. Planner picks the exact insertion site. The wording above is the canonical message — keep it identical across all call sites for grep-ability.

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| Classic PATs (`ghp_...`) with full account-wide scope | Fine-grained PATs (`github_pat_...`) scoped to specific repos with granular permissions | GitHub blog announcement 2022-10-18; generally available 2024 | Fine-grained PATs are now the GitHub-recommended default for private-repo scripted access. Same Bearer header format. |
| Token-in-URL for git clone of private repos (`https://$TOKEN@github.com/...`) | Authorization header on REST API calls | Long-standing best practice but reaffirmed by GitHub community 2023 onward | Token-in-URL exposes the PAT in process listings and shell history. Auth header is the canonical pattern. |
| `application/vnd.github.v3+json` Accept header | `application/vnd.github+json` (no version suffix) + `X-GitHub-Api-Version` header | GitHub deprecated the `.v3` suffix; new pattern is `+json` with explicit version header per [CITED: https://docs.github.com/en/rest/authentication/authenticating-to-the-rest-api] | `vnd.github.raw+json` is the current spelling. The `.v3` form still works but is legacy. |
| `Microsoft.NET.Sdk` requiring explicit `<Compile Include>` for every source file | Auto-glob `**/*.cs` from SDK defaults | .NET Core 1.0 era; well established | Modern csproj files are typically 20-30 lines, not 200+. Existing `Jaminator.csproj` is already SDK-style. |

**Deprecated/outdated:**

- `application/vnd.github.v3.raw` (or `.v3+json`) — superseded by `vnd.github.raw+json` + `X-GitHub-Api-Version` header. Old form still works but isn't the documented current pattern.
- Token-in-URL (`https://x-access-token:$TOKEN@github.com/...`) for fetching content — works for `git clone` but not for the REST API; Authorization header is correct for API calls.

## Assumptions Log

> List of claims tagged `[ASSUMED]` in this research that need user confirmation before execution.

| # | Claim | Section | Risk if Wrong |
|---|-------|---------|---------------|
| A1 | Operator (Zach / Jam Coding) will use a flat-list secrets path `secrets.json` at the private repo root, NOT a nested path like `manifest/secrets.json`. The exact path is operator-determined and goes into `wifi-secrets-url.txt`. | Architecture diagram, code examples | Low — only the operator-written URL file changes; code is path-agnostic. |
| A2 | Private repo branch is `main`. The `?ref=main` query parameter is hardcoded into the URL. | Pattern 1, code examples | Low — operator can put any branch in `wifi-secrets-url.txt` (e.g., `?ref=production`). |
| A3 | Fleet size is < 1000 laptops, with logon-per-day patterns implying < 10,000 API calls/day per PAT. Rate-limit ceiling of 5,000 req/hr is sufficient. | Pattern 1 / Rate limits | Low — even at 10× the assumed fleet, we're still well under the limit. |
| A4 | The `Generated/` directory under `src/Jaminator/` is picked up by the existing csproj's SDK auto-glob without modification. Verified mechanically by `dotnet msbuild -preprocess`. | Pattern 2 / Pitfall 6 | Medium — if auto-glob skips it, we need an explicit `<Compile Include>` — easy fix discovered at first build attempt. Planner should add this verification as a task step. |
| A5 | `File.Delete` followed by `File.Move` is "atomic enough" for the cache-pair scenario — i.e., the brief window where the destination doesn't exist is acceptable. | Pattern 3 | Low — both operations are syscall-level fast (<<1ms); the failure mode is "two consecutive crashes during the millisecond between delete and move" which is vanishingly rare. Document the mode in code comment. |
| A6 | Operators are competent with PowerShell and can place a file in `installer/secrets/` or set an env var without further hand-holding. The README provides recipe-level instructions. | `installer/secrets/README.md` design | Low — Jam Coding's build operator IS the dev who shipped M1; assumption holds. |

**If this table is empty:** N/A — six assumptions logged. All are LOW or MEDIUM risk; A4 is the only one with non-trivial blast radius and has a one-minute verification step.

## Open Questions

1. **Should the debug log line ALSO be emitted in `--login-mode` and `--run-all` modes, or only in interactive UI mode?**
   - What we know: Success Criterion 5 says "verified by a debug log line ... when launched on the dev box." Doesn't specify mode.
   - What's unclear: Whether silent logon-time runs should write this same line to the daily log.
   - Recommendation: Emit it in **all** modes. The log message is harmless (PSKs are masked), and having a single grep-able marker for "fetch completed successfully" is valuable for debugging future logon-time issues. Planner picks the call-site that covers all modes.

2. **What exactly goes in `installer/secrets/wifi-secrets-url.txt`?**
   - What we know: It's the URL `ManifestFetcher` fetches with the bearer token. The operator writes the value out-of-band.
   - What's unclear: Whether the operator stores the full API URL (`https://api.github.com/repos/.../contents/secrets.json?ref=main`) or just a path fragment (`.../secrets.json?ref=main`) and the code constructs the rest.
   - Recommendation: Full URL. Less ambiguity, less code, less coupling between the operator's understanding of where the file lives and the binary's URL-construction logic. The operator-facing README documents the exact form: `https://api.github.com/repos/<org>/<repo>/contents/<path-to-secrets>?ref=<branch>`.

3. **Does the test SSID survive the dev-laptop smoke test if the laptop is already on it via a manual Windows credential?**
   - What we know: Phase 2 only verifies the manifest LOAD path, not the deploy path (deploy is Phase 3).
   - What's unclear: Whether the debug log line and joined Manifest in memory is the full Phase 2 verification, or whether some side-channel confirmation is also needed.
   - Recommendation: The debug log line IS the Phase 2 verification. Wi-Fi profile deployment is Phase 3; Phase 2's bar is "the code observes the joined `Manifest` correctly." No netsh invocation in Phase 2.

4. **For the `Generated/` directory, should `installer/build.ps1` emit a stale-removal step that deletes the file at script start?**
   - What we know: Every build overwrites the file via `Set-Content`.
   - What's unclear: If a previous build left a stale `BuildSecrets.g.cs` with a different PAT, and the current build fails before reaching the `Set-Content` step (e.g., `wix build` fails), is there a risk the developer hand-runs `dotnet build` and picks up the stale PAT?
   - Recommendation: No active deletion needed because `Set-Content` overwrites unconditionally. But add a comment to the operator README: "If you change the PAT, run `installer/build.ps1` end-to-end — don't try to clean up `Generated/` manually."

## Environment Availability

| Dependency | Required By | Available | Version | Fallback |
|------------|------------|-----------|---------|----------|
| .NET SDK 8+ | `dotnet build` for the EXE | Required on Windows build box (already confirmed in M1 D-09) | 8.x | — (no fallback; Windows-build-only is a constraint) |
| WiX 4 CLI | `wix build` for the MSI | Required on Windows build box (already confirmed in M1 D-09) | 4.x | — |
| Newtonsoft.Json 13.0.3 | Manifest + secrets deserialisation | Already a project dependency in `Jaminator.csproj` line 35 | 13.0.3 | — |
| PowerShell 5+ | `installer/build.ps1` execution | Available on every Windows box (built-in) | 5.1 (Windows PowerShell) or 7+ (PowerShell Core) | — |
| Network access to api.github.com from build box | Verifying PAT works during pre-release smoke test | Should be available; not a build dependency itself | — | If air-gapped: still bakes the PAT in; just can't smoke-test until deploy. |
| Fine-grained PAT | Build-time injection | Operator-generated, written to `installer/secrets/wifi-pat.txt` | — | Build fails fast with clear error if absent. |
| Private GitHub repo with `secrets.json` | Runtime fetch (not build-time) | Operator-created before Phase 2 execution begins (STATE.md pending todo) | — | Build succeeds without the repo existing; runtime fetch fails on first launch if repo is missing or `secrets.json` is empty. |
| Test SSID + PSK on dev laptop | Phase 2 smoke test (Success Criterion 5) | Operator-determined; the SSID must exist in the public manifest, the PSK must exist in the private `secrets.json` | — | Without a test SSID, Phase 2 success criterion 5 cannot be verified. STATE.md pending todo. |

**Missing dependencies with no fallback:**
- Private GitHub repo standup, fine-grained PAT generation, test SSID identification — all three are STATE.md "pending todos" that operator must complete BEFORE Phase 2 plan execution begins.

**Missing dependencies with fallback:**
- None — the operator todos above are blocking. CONTEXT.md `<domain>` block already flags this with the "Pre-execution operator checklist" note.

## Security Domain

> Phase 2 explicitly carries a credential delivery mechanism. ASVS review is required.

### Applicable ASVS Categories

| ASVS Category | Applies | Standard Control |
|---------------|---------|-----------------|
| V2 Authentication | Yes | Fine-grained PAT with `Contents: Read` scope, repo-restricted, termly rotation. Bearer auth via Authorization header (not URL embedding). |
| V3 Session Management | No | No user sessions in Jaminator. PAT is a static credential, not session-derived. |
| V4 Access Control | Yes | PAT is repo-scoped to ONLY the private secrets repo. No org access, no other repos. Read-only. |
| V5 Input Validation | Yes (limited) | Secrets JSON is a flat string→string map; Newtonsoft enforces structure. SSID lookup is exact-match; no path or shell injection surface. |
| V6 Cryptography | No | Threat model is operational opacity, not cryptographic protection. PAT is RE-recoverable from the public MSI by design. PROJECT.md WIFI-03 decision is the locked acceptance of this tradeoff. **Do NOT add encryption — it would be theater.** |
| V7 Error Handling | Yes | Loud fail on private-fetch failure (D-11). No silent half-state. PAT is never logged. Exception messages never include the PAT (sanitize at catch site if needed). |
| V8 Data Protection | Yes | PSKs masked as `***` in all log paths. Cache files written under `%ProgramData%\Jaminator\cache\` which is read-only to non-admins by default ACL inheritance. |
| V9 Communication | Yes | All fetches over HTTPS to api.github.com. .NET 4.8 default TLS 1.2+ behaviour. No certificate pinning (HARDEN-04 is M3). |
| V10 Malicious Code | No | No user-supplied code paths. Secrets JSON is data, not executable. |
| V14 Configuration | Yes | PAT is in a gitignored file with operator-only access. The Generated/ directory is gitignored. CI doesn't exist yet (HARDEN-07 for M3) — for now, the build operator's local machine ACLs are the configuration security boundary. |

### Known Threat Patterns for .NET 4.8 / WinForms / single-EXE deployment

| Pattern | STRIDE | Standard Mitigation |
|---------|--------|---------------------|
| PAT extraction from EXE via static analysis (strings, RE) | Information Disclosure | **Accepted**. Threat model is operational, not cryptographic. Mitigation: termly rotation aligned with PSK rotation. PROJECT.md Key Decisions row WIFI-03. |
| PAT exfil via process memory dump on a fleet laptop | Information Disclosure | **Accepted**. Same as above — any local admin can extract. Same mitigation. |
| PSK exposure via log files on disk | Information Disclosure | Mask as `***` in all log emission sites. Pre-emptive `WifiProfileEntry.ToString()` override (D-15). |
| PSK exposure via crash dump | Information Disclosure | Same masking applies — no log path or string-format path retains the PSK as plaintext. Crash dump would contain the in-memory `WifiProfileEntry.Psk` field; .NET 4.8 doesn't natively redact memory. **Accepted** (low risk; same exposure level as the deployed `netsh wlan show profile key=clear`). |
| Public manifest tampered to declare a wildcard SSID | Tampering | Public manifest is in `zachlagden/jaminator` (push-restricted). Manifest fetch is over HTTPS. Phase 2 doesn't add tamper detection beyond what already exists (HARDEN-01 / HARDEN-02 are M3). |
| Private repo cloned by an unauthorised reader of the PAT | Information Disclosure of PSKs | Mitigation: PAT is **read-only** with `Contents: Read` scope. Adversary can read PSKs but cannot tamper. PSK exposure is the threat we're already mitigating against by moving PSKs off the public internet — moving them behind a PAT raises the bar but doesn't make them inviolable. **Accepted**. |
| Cache file (`%ProgramData%\Jaminator\cache\secrets.json`) readable by non-admin | Information Disclosure | `%ProgramData%` is admin-writable but world-readable by default on Windows. PSKs in the cache file are readable by any local user. Mitigation options: (a) `File.SetAccessControl` to admin-only — possible but adds NT-API surface to the fetcher; (b) accept and document — local user could also run `netsh wlan show profile key=clear` to get the same PSK. Recommend (b) — accept and document in `docs/manifest-schema.md` under a "Security threat model" subsection. Aligned with PROJECT.md WIFI-03's operational-not-cryptographic stance. |
| PAT logged accidentally in build output | Information Disclosure | `installer/build.ps1` never echoes the PAT. Verify by code review: no `Write-Host $wifiPat` or `Write-Output $wifiPat` calls. The generation step's success message can say "Generated <file>" but not the value. |
| PAT committed to git by accident | Information Disclosure of PAT | `.gitignore` rules for `installer/secrets/*` and `src/Jaminator/Generated/` catch the two known leakage paths. A pre-commit hook checking for `github_pat_` prefix string in staged diffs would be ideal — defer to operator's git workflow (out of scope for Phase 2 code, in scope for `installer/secrets/README.md`). |
| PAT in CI logs | Information Disclosure | No CI in v0.8.0 (HARDEN-07 is M3). When CI lands, the standard mitigation is GitHub Actions / Azure DevOps secret variables which are auto-redacted in logs. |

## Sources

### Primary (HIGH confidence)

- [GitHub Docs — REST API endpoints for repository contents](https://docs.github.com/en/rest/repos/contents) — Confirmed: `Accept: application/vnd.github.raw+json` returns raw bytes; `?ref=` selects branch; endpoint works with fine-grained PATs requiring `Contents: Read`. Max file size 100 MB.
- [GitHub Docs — Authenticating to the REST API](https://docs.github.com/en/rest/authentication/authenticating-to-the-rest-api) — Confirmed: `Authorization: Bearer <TOKEN>` is the canonical header; `Authorization: token` also works but Bearer is the documented current form. `X-GitHub-Api-Version: 2026-03-10` recommended for stability.
- [GitHub Docs — Permissions required for fine-grained PATs](https://docs.github.com/en/rest/authentication/permissions-required-for-fine-grained-personal-access-tokens) — Confirmed: `Contents: Read` is the minimum for the contents endpoint. Metadata auto-included.
- [GitHub Docs — Rate limits for the REST API](https://docs.github.com/en/rest/using-the-rest-api/rate-limits-for-the-rest-api) — Confirmed: 5,000 req/hr per authenticated PAT; secondary limit of 100 concurrent.
- [Microsoft Learn — .NET project SDK overview](https://learn.microsoft.com/en-us/dotnet/core/project-sdk/overview) — Confirmed: default Compile glob is `**/*.cs`; excludes are `**/*.user; **/*.*proj; **/*.sln(x); **/*.vssscc`; obj/bin excluded via `DefaultItemExcludes`.
- [Microsoft Learn — NETSDK1022: Duplicate items were included](https://learn.microsoft.com/en-us/dotnet/core/tools/sdk-errors/netsdk1022) — Confirmed: explicit Compile Include that duplicates auto-glob → build error.
- [Microsoft Learn — System.IO.File.Replace](https://learn.microsoft.com/en-us/dotnet/api/system.io.file.replace) — Confirmed: atomic replacement available on .NET 4.8.
- [Newtonsoft.Json — Deserialize a Dictionary](https://www.newtonsoft.com/json/help/html/DeserializeDictionary.htm) — Confirmed: `JsonConvert.DeserializeObject<Dictionary<string, string>>(json)` handles `{ "key": "value", ... }` directly.
- [Git Docs — gitignore](https://git-scm.com/docs/gitignore) — Confirmed: directory-contents-then-negate pattern (`dir/*` + `!dir/file`); can't re-include if parent dir is excluded.

### Secondary (MEDIUM confidence)

- [GitHub Community Discussion #160828 — raw.githubusercontent.com auth](https://github.com/orgs/community/discussions/160828) — Community-confirmed: raw.githubusercontent.com ignores Authorization header. Critical to Phase 2 design.
- [GitHub Community Discussion #36459 — Read Only Access Token to a raw file from a Private repository](https://github.com/orgs/community/discussions/36459) — Same finding; recommends REST API contents endpoint as the canonical pattern.
- [GitHub Blog — Introducing fine-grained personal access tokens](https://github.blog/security/application-security/introducing-fine-grained-personal-access-tokens-for-github/) — Confirmed: repo-scoped tokens with permission-granular access.
- [antonymale.co.uk — Atomic File Writes on Windows](https://antonymale.co.uk/windows-atomic-file-writes.html) — Confirms `File.Replace`'s atomic delegation to `ReplaceFile()` on Windows.
- [dotnet/runtime#18034 — Implement File.Replace() as a documented safe atomic API](https://github.com/dotnet/runtime/issues/18034) — Confirms File.Replace atomicity on Windows.
- [Code Maze — How to Add a BearerToken to an HttpClient Request](https://code-maze.com/add-bearertoken-httpclient-request/) — Confirms `AuthenticationHeaderValue("Bearer", token)` pattern for `HttpRequestMessage.Headers.Authorization`.
- [KirillOsenkov gist — Generating a .cs file during build and adding it to compilation](https://gist.github.com/KirillOsenkov/f20cb84d37a89b01db63f8aafe03f19b) — Confirms MSBuild target alternative for generated files (we chose PS-side instead).

### Tertiary (LOW confidence — not relied upon)

- None — all critical claims are backed by primary sources.

## Metadata

**Confidence breakdown:**

- Standard stack: HIGH — every package and version is verified in the existing csproj or via nuget.org.
- Architecture (GitHub API endpoint, PAT scope, Bearer auth): HIGH — multiple primary sources (GitHub docs + community confirmation).
- Architecture (atomic dual-file cache): HIGH for the per-file pattern; MEDIUM for the cross-file consistency window (no perfect atomic-pair on Windows without WAL).
- Architecture (SDK auto-glob picks up Generated/): HIGH for the default behaviour; verification step (`dotnet msbuild -preprocess`) added to mitigate the LOW-probability case where a project-local config overrides the default.
- Pitfalls: HIGH — most are derived from the GitHub-API/Bearer flow which has well-documented gotchas.
- Code examples: HIGH — all patterns are taken from verified primary sources or the existing codebase.

**Research date:** 2026-05-11

**Valid until:**
- Stable claims (the .NET 4.8 / SDK / Newtonsoft / git patterns): 90+ days.
- GitHub API endpoint shape and PAT behaviour: 30 days — GitHub occasionally tweaks rate-limit or media-type behaviour; re-verify before any future related work.
- Specifically: re-verify the `X-GitHub-Api-Version` value (`2026-03-10`) is still current at Phase 5 ship time; the value embedded in code should match the version validated at production-PAT-build time.

---

*Phase 2 research complete. Planner: see `## Architectural Responsibility Map` for task-tier assignment and `## Architecture Patterns` for the four canonical patterns this phase introduces.*
