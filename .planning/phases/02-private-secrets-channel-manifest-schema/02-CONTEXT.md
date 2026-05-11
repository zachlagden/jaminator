# Phase 2: Private secrets channel + manifest schema - Context

**Gathered:** 2026-05-11
**Status:** Ready for planning
**Mode:** --auto (recommended option selected for every gray area, logged inline)

<domain>
## Phase Boundary

**In scope:**
- Define a `WifiEntry` / `WifiProfileEntry` DTO pair in `src/Jaminator/Models/Manifest.cs` covering the WIFI-01 schema (SSID, auth mode, hidden, autoConnect, scope, PSK) and wire it into the top-level `Manifest` as `wifi`.
- Define the **private** secrets file shape (an SSID→PSK map) and document it in `docs/manifest-schema.md` alongside the public `wifi.profiles[]` block.
- Stand up the build-side machinery: a `installer/secrets/wifi-pat.txt` (gitignored) and/or `JAMINATOR_WIFI_PAT` env var read by `installer/build.ps1`, plus a generated `BuildSecrets.g.cs` baked into the EXE at build time that carries the PAT and the private secrets URL as `internal const` strings.
- Extend `src/Jaminator/Services/ManifestFetcher.FetchAsync` to dual-fetch (public manifest from the existing URL + private `secrets.json` from the baked URL with `Authorization: Bearer <PAT>`), join SSID→PSK into the in-memory `WifiProfileEntry` list, cache **both** files under `%ProgramData%\Jaminator\cache\` as a joined pair, and fall back to the cached pair when either online fetch fails.
- Emit a debug log line at startup showing the joined profile count and SSID list (PSKs masked as `***`) so the dev-laptop smoke test in Success Criterion 5 is verifiable.
- Add the test SSID + PSK to the private `secrets.json` so the joined manifest has at least one fully-loaded profile.

**Out of scope (handled by Phase 3, 4, 5):**
- `WifiProfileRunner` and `netsh wlan add profile` invocation — Phase 3.
- "wifi" `SectionPanel` in the WinForms UI and login-mode wiring — Phase 3.
- Idempotency (`netsh wlan show profile` diff-and-skip), failure isolation, end-to-end smoke test — Phase 4.
- Schema-version validation of the new `wifi` field (HARDEN-02), parallel logon-path I/O (HARDEN-06), automated PAT-rotation CI (HARDEN-07) — Milestone 3.
- Version bump to `0.8.0`, MSI build with production PAT, git tag, GitHub Release — Phase 5.

**Covers requirements:** WIFI-01, WIFI-03

**Pre-execution operator checklist (not code — must happen before planner kicks off execution):**
1. Stand up the private GitHub repo (e.g., `jamcoding-internal/jaminator-secrets`) with an initial `secrets.json`.
2. Generate a fine-grained read-only PAT scoped to that repo's `Contents: Read` only; record expiry for the rotation runbook.
3. Decide the dev-laptop **test** SSID + PSK and add it to both the public manifest (`wifi.profiles[]` entry without PSK) and the private `secrets.json` (`{ "TestSSID": "TestPSK" }`).

</domain>

<decisions>
## Implementation Decisions

### Private `secrets.json` shape

- **D-01:** The private repo's `secrets.json` is a **flat JSON object keyed by SSID, value = PSK** — `{ "SchoolNet-Year3": "...", "SchoolNet-Staff": "..." }`. No envelope, no `schemaVersion` field, no nested object.
  - **[auto] Q: How should the private `secrets.json` be structured? → Selected: Flat SSID→PSK map (recommended). Alternatives considered: mirror the public manifest's `wifi.profiles[]` shape with PSKs filled in / wrap in `{ schemaVersion, wifiPsks: { … } }` envelope. Rejected because (a) REQUIREMENTS.md WIFI-03 explicitly specifies "keyed by Wi-Fi SSID → PSK", (b) the public manifest is the single source of truth for profile metadata (auth mode, hidden, autoConnect, scope) — duplicating it in the private file invites drift, (c) non-technical Jam Coding staff need to rotate a PSK by editing one obvious line, not a profile DTO, (d) schema versioning lives on the public manifest where it already exists; a future non-Wi-Fi secret class warrants its own file, not a multiplexed envelope here.**

### `WifiEntry` / `WifiProfileEntry` DTO shape (`src/Jaminator/Models/Manifest.cs`)

- **D-02:** Add a **top-level wrapper** `WifiEntry` with a `Profiles: List<WifiProfileEntry>` field, mirroring the existing `Cleanup` / `Wallpaper` / `Schedule` pattern. Surface it on `Manifest` as `[JsonProperty("wifi")] public WifiEntry? Wifi { get; set; }` (nullable — manifests without Wi-Fi sections continue to deserialise cleanly).
  - **[auto] Q: How should the WIFI DTOs nest under `Manifest`? → Selected: nested wrapper (recommended). Alternative considered: flat `Manifest.Wifi: List<WifiProfileEntry>` skipping the wrapper. Rejected because the existing codebase pattern (Cleanup, Wallpaper, Schedule) consistently uses a wrapper, leaving room to add policy fields later (`wifi.enforceAll`, `wifi.deletePsksOnUninstall`) without restructuring DTO consumers; ROADMAP success criterion 3 says "wired into the top-level `Manifest` class as `wifi`" which the wrapper satisfies.**

- **D-03:** `WifiProfileEntry` fields with `[JsonProperty]` snake-key bindings, matching WIFI-01 verbatim:
  - `Ssid: string` → `"ssid"`
  - `AuthMode: string` → `"authMode"` (values: `"WPA2PSK"`, `"WPA3PSK"`, `"open"` — string, not enum, to keep manifest authoring forgiving)
  - `Hidden: bool` → `"hidden"` (default `false`)
  - `AutoConnect: bool` → `"autoConnect"` (default `true`)
  - `Scope: string` → `"scope"` (values: `"all-users"` (default), `"current-user"`)
  - `Psk: string?` → `"psk"` (nullable in the DTO; **never set from the public manifest**; populated in memory at join time from `secrets.json`)
  - **[auto] Q: Should `AuthMode` / `Scope` be enums or strings? → Selected: strings (recommended). Alternative considered: typed enums. Rejected because the existing codebase uses strings for similar schema fields (`ArchEntry.Kind = "msi"|"exe"|"zip-extract"`, `CommandEntry.Shell = "powershell"|"cmd"`) — consistency wins; the runner in Phase 3 validates the value at use site.**

### PAT build-time storage

- **D-04:** `installer/build.ps1` resolves the PAT in this order: (1) read `installer/secrets/wifi-pat.txt` if present (file is `.gitignore`'d via a new `installer/secrets/` rule); (2) read `$env:JAMINATOR_WIFI_PAT` if the file is absent; (3) fail-fast with a clear message ("`installer/secrets/wifi-pat.txt` not found AND `$env:JAMINATOR_WIFI_PAT` not set — cannot embed Wi-Fi PAT. See docs/manifest-schema.md §Private secrets channel for setup.").
  - **[auto] Q: Where does the PAT live on the build box? → Selected: file-first, env-var fallback (recommended). Alternatives considered: file-only / env-var-only. Rejected because file-only is hostile to future CI (HARDEN-07 will read from CI secrets via env var); env-var-only is hostile to interactive Windows builds where the dev hasn't set a persistent env var. STATE.md already lists both as acceptable. The combined precedence matches how widely-used tools (gh CLI, AWS CLI) resolve credentials.**

- **D-05:** Add `installer/secrets/` to `.gitignore` (NEW rule). Add `installer/secrets/.keep` (empty, `git add -f`'d) so the directory exists in the repo without leaking the PAT. Add a `installer/secrets/README.md` (committed) explaining "place `wifi-pat.txt` here; this directory is `.gitignore`'d; see `docs/manifest-schema.md` for PAT scope and rotation".

### PAT injection into the EXE

- **D-06:** `installer/build.ps1` generates `src/Jaminator/Generated/BuildSecrets.g.cs` **before** `dotnet build` runs. The file contains:
  ```csharp
  // Auto-generated by installer/build.ps1 — do not edit, do not commit.
  namespace Jaminator
  {
      internal static class BuildSecrets
      {
          internal const string WifiPat = "@@PAT@@";
          internal const string SecretsUrl = "@@URL@@";
      }
  }
  ```
  with the two placeholders replaced at generation time. The `Generated/` directory is added to `.gitignore`. `Jaminator.csproj` either auto-globs `**/*.cs` (default SDK behaviour) so the generated file is compiled without extra config, or an `<ItemGroup><Compile Include="Generated/**/*.cs" /></ItemGroup>` block is added explicitly — planner picks based on the current csproj.
  - **[auto] Q: How is the PAT embedded in the EXE? → Selected: generated `internal const string` (recommended). Alternatives considered: `[assembly: AssemblyMetadata("WifiPat", "…")]` attribute / `<EmbeddedResource>` read via `Assembly.GetManifestResourceStream` / companion file in MSI payload. Rejected because (a) `const string` is the simplest call-site (`BuildSecrets.WifiPat` from `ManifestFetcher`), no reflection or stream parsing, (b) `AssemblyMetadata` would require reflection at every call, (c) embedded resource adds runtime parsing surface for zero security gain (the threat model already accepts RE recovery — locked in PROJECT.md Key Decisions), (d) companion file in MSI payload puts the secret in a file outside the EXE which is *easier* to recover than from the EXE's `.text` section, marginally worse. Success Criterion 5 explicitly says "implementation detail chosen during planning" — auto-mode picks `const string`; planner may refine if csproj globbing turns out non-trivial.**

- **D-07:** `BuildSecrets.WifiPat` and `BuildSecrets.SecretsUrl` are `internal const` so they're inlined into call-site IL and there is no convenient extension point for swapping them at runtime. This matches the threat model (operational opacity, not cryptographic protection) without adding a class of "configure my own PAT at runtime" backdoor.

### Private repo URL discovery

- **D-08:** The private secrets URL is **baked into the EXE at build time** via `BuildSecrets.SecretsUrl` (D-06), not stored in source and not declared in the public `manifest.json`. `installer/build.ps1` reads it from the same precedence chain as the PAT: `installer/secrets/wifi-secrets-url.txt` (gitignored) → `$env:JAMINATOR_WIFI_SECRETS_URL` → fail-fast.
  - **[auto] Q: How does Jaminator know where to fetch `secrets.json`? → Selected: baked into the EXE at build time (recommended). Alternatives considered: hardcoded string constant in source / field in the public `manifest.json` (`wifi.secretsUrl`). Rejected because (a) hardcoding in source forces an org rename or repo migration to bleed into the source tree, (b) declaring the secrets URL in the public manifest reveals the private repo's existence to anyone reading the public manifest (small but unnecessary; the PAT remains the real gate), (c) the URL is paired with the PAT it requires — rotating the endpoint should be the same operation as rotating the PAT, and (a, b) both decouple them.**

### Dual-fetch ordering, caching, and failure modes (`ManifestFetcher`)

- **D-09:** Fetch order is **sequential, public-first, then private**. Public-first because it carries the profile metadata (SSID list, auth mode); private second because its only job is to fill in PSKs by SSID lookup.
  - **[auto] Q: Sequential or parallel fetch? → Selected: sequential public-first (recommended). Alternative considered: `Task.WhenAll` parallel fetch. Rejected for v0.8.0 because (a) failure-mode reasoning is easier when ordering is explicit, (b) the time saving is ~200ms on a working connection and irrelevant on a degraded one, (c) HARDEN-06 (M3) plans a parallel logon-path I/O pass that can revisit this with the wallpaper fetch and other concurrent work in a unified design.**

- **D-10:** **Both fetches succeeding** → in-memory join `wifi.profiles[i].psk = secrets[wifi.profiles[i].ssid]` for every profile whose SSID has a `secrets.json` entry; profiles with no matching SSID get `Psk = null` and log an `Info`-level "profile declared but no PSK in private channel; skipping at runtime" (Phase 3's runner will skip these). Cache both files: `%ProgramData%\Jaminator\cache\manifest.json` (existing path, unchanged) and `%ProgramData%\Jaminator\cache\secrets.json` (NEW), written atomically as a pair (write `.tmp` then `Move`) so a crash mid-write can't leave a stale-public + new-private cache or vice versa.

- **D-11:** **Either online fetch failing** → fall back to the **joined cache pair**. If both cached files exist, deserialise them and join (same logic as the online path). If either cached file is missing or corrupt, throw `InvalidOperationException` with a message naming which file failed and why — same shape as the existing single-fetch failure path. The fetch never silently returns a half-populated manifest.
  - **[auto] Q: Private-fetch failure behaviour? → Selected: fatal (with joined-cache fallback) (recommended). Alternative considered: non-fatal, proceed with empty `wifi.profiles` and log warning. Rejected because (a) a public manifest declaring an SSID with no matching PSK in the private channel means the runner can either silently skip (leaving the laptop on the wrong network) or attempt an open-network connection (security regression). Loud failure at fetch time is the safer default, (b) cache fallback handles the "offline at logon" case which is the legitimate non-error scenario, (c) STATE.md's "Wi-Fi access is login-safe" applies to runtime behaviour, not to silently masking a configuration error.**

- **D-12:** The existing `FetchAsync` return type `(Manifest manifest, bool fromCache)` stays. `fromCache: true` means **both** files were served from cache (the joined pair). There is no "half from cache" return — if either online fetch fails, both files come from cache or the call throws.

### Bearer-auth HttpClient strategy

- **D-13:** Reuse the existing static `HttpClient Http` field in `ManifestFetcher`. Attach `Authorization: Bearer <BuildSecrets.WifiPat>` per-request via `HttpRequestMessage`, not as a default header on the shared client. This keeps a single `HttpClient` for both fetches without leaking the PAT into a process-global default header (which could accidentally be sent to the public manifest URL on a misconfigured retry).
  - **[auto] Q: How is the bearer header attached? → Selected: per-request `HttpRequestMessage` (recommended). Alternative considered: a second static `HttpClient` with `DefaultRequestHeaders.Authorization` set. Rejected because process-global default headers are a known footgun (any consumer of that `HttpClient` instance inherits them); per-request is explicit and local to the call that needs the auth.**

- **D-14:** Send the standard `User-Agent: Jaminator/<version>` header on both requests (GitHub's raw content API rejects unidentified callers under some abuse-mitigation paths). Version comes from `Program.ToolVersion`.

### PSK masking in logs

- **D-15:** Anywhere a `WifiProfileEntry.Psk` is written to a log (Logger.Info / Warn / Error), replace it with the literal string `***`. Apply in `ManifestFetcher`'s post-join debug line (the "joined N Wi-Fi profiles: [SSID-A, SSID-B, …]" message specified in Success Criterion 5) and pre-emptively in `WifiProfileEntry.ToString()` override (so future log call sites in Phase 3/4 can't accidentally leak via `$"{entry}"` string interpolation).
  - **[auto] Q: How are PSKs masked when logged? → Selected: fixed `***` (recommended). Alternatives considered: length-preserving `********` (8 chars) / first-and-last char `p***d`. Rejected because (a) length leakage gives a brute-force attacker a search-space hint, (b) first/last char is the worst of both — leaks information for negligible debuggability gain.**

### Commit strategy

- **D-16:** Atomic commits per file-area, in this order (each independently builds cleanly):
  1. `feat(manifest): add Wifi/WifiProfile DTOs to Manifest model` — `src/Jaminator/Models/Manifest.cs` only.
  2. `build(installer): add gitignored secrets directory and PAT resolution script` — `.gitignore`, `installer/secrets/.keep`, `installer/secrets/README.md`, `installer/build.ps1` PAT-resolution + `BuildSecrets.g.cs` generation.
  3. `feat(fetcher): dual-fetch public manifest and private secrets with PSK join` — `src/Jaminator/Services/ManifestFetcher.cs`.
  4. `docs(manifest-schema): document wifi.profiles[] and the private secrets channel` — `docs/manifest-schema.md`.
  5. `chore(test): add startup debug log line for joined Wi-Fi profile count` — `src/Jaminator/Program.cs` (one-line debug-only log + masked SSID list).

### Claude's Discretion

- Exact MSBuild wiring for picking up `src/Jaminator/Generated/BuildSecrets.g.cs` — auto-glob vs explicit `<Compile Include>`. Planner picks after inspecting `Jaminator.csproj`.
- Whether `installer/build.ps1` writes the generated `BuildSecrets.g.cs` **before** the `dotnet build` invocation (current build.ps1 calls `dotnet build` inside the script) or whether a separate MSBuild pre-build target consumes the PAT from env. The cleanest minimal change is "generate the file from PS first, then call `dotnet build`" — but planner has latitude here.
- Whether the joined-cache atomic-write is a single combined-JSON file (write a `{ "manifest": …, "secrets": … }` envelope to one file) or two `.tmp` + `Move` operations on the two existing-style files. Either satisfies the "no half-write" goal; planner picks based on rollback complexity.
- Choice of `JsonConvert.DeserializeObject<Dictionary<string, string>>(secretsJson)` vs a typed `Secrets` wrapper class for `secrets.json` — both are fine; flat Dictionary is simpler and matches D-01.

</decisions>

<canonical_refs>
## Canonical References

**Downstream agents (researcher, planner, executor) MUST read these before planning or implementing.**

### Project planning artifacts (LOCKED — re-read each session)

- `.planning/PROJECT.md` — **Key Decisions table**, specifically the WIFI-03 row (private-repo + PAT-in-MSI, threat model is operational not cryptographic). Constraints block (Windows-only build, manual smoke test, no automated test scaffolding in M2).
- `.planning/REQUIREMENTS.md` — **WIFI-01** (manifest schema additions), **WIFI-03** (private channel + PAT bundling), the Out-of-Scope table (encryption-at-rest explicitly rejected), and the v2 deferred items relevant to M3 (HARDEN-02 schema-version validation, HARDEN-06 parallel I/O, HARDEN-07 PAT rotation automation).
- `.planning/ROADMAP.md` — **Phase 2 success criteria** (lines 32-37) which define the verification bar.
- `.planning/STATE.md` — Accumulated context: decisions, pending operator todos (private repo standup, PAT generation, test SSID/PSK identification), blockers (Windows build env, GPO override possibility).

### Prior phase decisions (carry forward)

- `.planning/phases/01-remove-broken-custom-action-and-improve-diagnostics/01-CONTEXT.md` — **D-11 commit pattern** (atomic commits per file-area, independently buildable) and **D-05 TEMP-log diagnostic pattern** (reuse in Phase 4's WIFI failure-isolation logs, not in Phase 2). M1 left the codebase in a known-good state at v0.7.5.

### Codebase reference (informs implementation, not policy)

- `.planning/codebase/STACK.md` — .NET 4.8 / Newtonsoft.Json 13.0.3 / `HttpClient` / WiX 4 confirmation.
- `.planning/codebase/ARCHITECTURE.md` — `Manifest` model layer, `ManifestFetcher` service responsibilities, dual-mode entry points.
- `.planning/codebase/INTEGRATIONS.md` — HTTP fetch integration with `raw.githubusercontent.com`; cache topology under `%ProgramData%\Jaminator\cache\`.
- `.planning/codebase/CONVENTIONS.md` — `[JsonProperty("snake_case")]` binding pattern, sealed-class DTOs, nullable reference types enabled, fail-open exception handling with logged context.
- `.planning/codebase/CONCERNS.md` — Background: TLS pinning + alternate manifest URL are M3 items; not in scope here but the new private-channel fetch should not regress what TLS posture already exists.

### Files edited or created in this phase

- `src/Jaminator/Models/Manifest.cs` — Add `WifiEntry`, `WifiProfileEntry`; wire `Manifest.Wifi`.
- `src/Jaminator/Services/ManifestFetcher.cs` — Dual-fetch + join + joined-cache + bearer-auth.
- `src/Jaminator/Program.cs` — Add debug log line "joined N Wi-Fi profiles: [...]" at startup (Success Criterion 5).
- `src/Jaminator/Generated/BuildSecrets.g.cs` — Generated, gitignored. Carries `WifiPat` + `SecretsUrl` consts.
- `src/Jaminator/Jaminator.csproj` — Verify auto-glob picks up `Generated/` or add explicit `<Compile Include>`.
- `installer/build.ps1` — PAT/URL resolution; `BuildSecrets.g.cs` generation; fail-fast on missing PAT.
- `installer/secrets/.keep` — Empty, committed (directory marker).
- `installer/secrets/README.md` — Operator-facing doc explaining PAT placement + rotation; committed.
- `installer/secrets/wifi-pat.txt` — Gitignored; written by operator out-of-band.
- `installer/secrets/wifi-secrets-url.txt` — Gitignored; written by operator out-of-band.
- `.gitignore` — Add `installer/secrets/*` with `!installer/secrets/.keep` and `!installer/secrets/README.md` negations, plus `src/Jaminator/Generated/`.
- `docs/manifest-schema.md` — Document `wifi.profiles[]` (public) and `secrets.json` (private) schemas, the PAT bearer auth mechanism, the cache topology, and the operator setup workflow (link to `installer/secrets/README.md`).

### Out-of-tree resources (operator action — not committed)

- The private GitHub repo (e.g., `https://github.com/jamcoding-internal/jaminator-secrets`) with `secrets.json` at its repo root (or `manifest/secrets.json` — planner picks; whichever path is in `wifi-secrets-url.txt` wins).
- A GitHub fine-grained PAT scoped **only** to the private repo with `Contents: Read` permission and no other repo or org scopes. Expiry date recorded for the rotation runbook.

### External docs (informational, not authoritative)

- GitHub's docs on fine-grained PATs and `Authorization: Bearer <token>` semantics for raw-content fetches — relevant when validating PAT scope and confirming the auth header format.
- Newtonsoft.Json 13.0.3 docs on `JsonConvert.DeserializeObject<Dictionary<string, string>>` and nullable property handling.

</canonical_refs>

<code_context>
## Existing Code Insights

### Reusable Assets

- **`ManifestFetcher.Http` static `HttpClient`** (`src/Jaminator/Services/ManifestFetcher.cs:18`) — Already configured with a 15-second timeout. Reuse it for the private-secrets fetch by attaching the bearer header per-request via `HttpRequestMessage`; do not create a second `HttpClient` instance (avoids socket-exhaustion footguns).
- **Existing cache topology** (`ManifestFetcher.cs:23-33`) — `%ProgramData%\Jaminator\cache\manifest.json` already exists. Add `secrets.json` alongside it under the same `cache` directory. The directory-creation helper (`Directory.CreateDirectory(dir)`) is already idempotent.
- **Cache-busting query string** (`ManifestFetcher.cs:40`) — `?t=<unix-timestamp>` is already appended to the public fetch URL to defeat CDN caching of `raw.githubusercontent.com`. Apply the same pattern to the private fetch.
- **Hierarchical-fallback exception messages** (`ManifestFetcher.cs:60-65`) — `InvalidOperationException` with "network failed AND cache corrupt" / "network failed, no cache" wording. Extend the same shape: which file (public manifest vs private secrets) and which path (network/cache) failed.
- **`Logger`** (`src/Jaminator/Services/Logger.cs`) — Thread-safe, append-only, already in use. The joined-profile debug log line (Success Criterion 5) lives here.
- **DTO pattern** (`src/Jaminator/Models/Manifest.cs`) — `[JsonProperty("snake_case")]` binding, sealed class, nullable reference types, `List<T> = new()` default initialisation. `WifiEntry` / `WifiProfileEntry` should follow this verbatim.

### Established Patterns

- **`[JsonProperty]` snake-case → C# PascalCase binding** — every existing DTO uses this; the new Wi-Fi DTOs must too.
- **Sealed DTO classes** — `WifiEntry`, `WifiProfileEntry` should both be `public sealed class`.
- **Nullable wrapper sections** — `Manifest.Wallpaper`, `Manifest.Cleanup`, `Manifest.Schedule` are all `WallpaperEntry?` / `CleanupEntry?` / `ScheduleEntry?`. `Manifest.Wifi` is `WifiEntry?` for symmetry.
- **`List<T> = new()` default** — `WifiEntry.Profiles` initialises to `new List<WifiProfileEntry>()` so deserialising a manifest with no `wifi.profiles` key gives an empty list, not null.
- **String-typed schema fields with documented enum-like values** — `ArchEntry.Kind = "msi" | "exe" | "zip-extract"` (line 57), `CommandEntry.Shell = "powershell" | "cmd"` (line 88). Apply the same convention to `WifiProfileEntry.AuthMode` and `WifiProfileEntry.Scope`.
- **Atomic-pair cache writes** — Not yet a codebase pattern; introduce it here as `.tmp` + `Move` per file to avoid mixed cache state (a Phase 2 invariant the runner in Phase 3 will rely on).

### Integration Points

- **`Program.Main` → `ManifestFetcher.FetchAsync`** — Currently called from one or more entry points; locate them in `src/Jaminator/Program.cs` to add the joined-profile debug log line immediately after a successful fetch.
- **`installer/build.ps1` → `dotnet build` → wix build** — `BuildSecrets.g.cs` must be generated **before** `dotnet build` invocation (line 30-31 in build.ps1). The MSI step (`wix build` at line 40) does not need to know about the PAT; it just packages whatever the build produced.
- **`Jaminator.csproj` glob** — Default `Microsoft.NET.Sdk` projects auto-include `**/*.cs`; verify this is in effect (no explicit `<Compile Remove>` for `Generated/`) so the generated file gets compiled without csproj edits.

</code_context>

<specifics>
## Specific Ideas

- **Joined-profile debug log line wording** (Success Criterion 5): `Joined manifest: {N} Wi-Fi profile(s) — [SSID-A, SSID-B] (PSKs: ***)`. Logged at `Info` level by `Logger` so it appears in `%ProgramData%\Jaminator\logs\jaminator-YYYYMMDD.log` and on the dev-laptop console during the smoke test.
- **Generated file header comment** for `BuildSecrets.g.cs`: `// Auto-generated by installer/build.ps1 at build time. Do not edit; do not commit. Contents are gitignored. Rotation: replace installer/secrets/wifi-pat.txt and rebuild.`
- **Operator README** (`installer/secrets/README.md`) target audience is the Jam Coding build operator, not students/teachers. Include: (a) where to drop `wifi-pat.txt`, (b) what permissions the PAT needs (Contents:Read on the private repo, nothing else), (c) when to rotate (each term, aligned with PSK rotation), (d) link to PROJECT.md Key Decisions for the threat model.
- **`docs/manifest-schema.md` additions**: split into two clearly-labelled blocks — "Public manifest (`manifest/manifest.json` in `zachlagden/jaminator`)" covering `wifi.profiles[]` without PSK fields, and "Private secrets (`secrets.json` in `jamcoding-internal/jaminator-secrets`, PAT-gated)" covering the SSID→PSK map shape. Explicitly call out that **PSKs never appear in the public manifest**.

</specifics>

<deferred>
## Deferred Ideas

### For Phase 3 (WifiProfileRunner + run-path integration)

- `WifiProfileRunner` service consuming `WifiProfileEntry` and invoking `netsh wlan add profile filename=<xml> user=<scope>`.
- "wifi" `SectionPanel` in MainForm (colour distinct from existing sections) and adding `"wifi"` to `LoginSafeSections`.
- Skip-with-warning behaviour for profiles where `Psk == null` after join (declared in public manifest but missing from private secrets).
- `WifiProfileEntry.ToString()` override with PSK masked — referenced in D-15 but the actual override lives in Phase 3 when the runner starts string-interpolating entries into log messages.

### For Phase 4 (idempotency, failure isolation, end-to-end smoke)

- `netsh wlan show profile name=<SSID> key=clear` diff-and-skip check; delete-then-add on drift.
- Per-Wi-Fi-failure `Jaminator-wifi-error-YYYYMMDDhhmmss.log` in `%TEMP%` mirroring the M1 DIAG-01 pattern.
- End-to-end smoke test: clean state → deploy → rotate PSK in private repo → re-deploy → idempotent third run.
- Documenting the operator PSK-rotation workflow in `docs/manifest-schema.md` (the "how a non-technical Jam Coding staffer rotates a Wi-Fi password" note).

### For Phase 5 (tag and ship v0.8.0)

- Bumping `Program.ToolVersion` to `"0.8.0"`.
- Production PAT placement on the Windows build box; running `installer/build.ps1` with the production secret; tagging `v0.8.0`; GitHub Release with the MSI asset and release notes covering the new schema + private-secrets workflow + PAT rotation procedure.

### For future milestones (M3 hardening)

- **HARDEN-02**: Schema-version validation — reject manifests with a newer `schemaVersion` than the tool understands. High priority post-M2 because we're adding `wifi` as a new top-level field.
- **HARDEN-06**: Parallel logon-path I/O — the M2 design adds a second network fetch per logon; HARDEN-06 will redo this concurrently with the wallpaper fetch in a unified design.
- **HARDEN-07**: CI workflow for automated PAT rotation + MSI rebuild — currently the rotation procedure is manual (drop new file, rebuild, re-ship); HARDEN-07 automates the rebuild side.
- **HARDEN-01**: Code-signing / Authenticode verification of downloaded third-party MSI/EXE installers — unrelated to Wi-Fi but listed in M3 backlog.
- **Persisting the cache as a single-file envelope** (`cache-bundle.json` containing both public and private payloads) — considered for D-10 atomicity but deferred; the two-file `.tmp + Move` approach is fine for v0.8.0 and the envelope approach would benefit from co-design with HARDEN-06's parallel-I/O work.

</deferred>

---

*Phase: 02-private-secrets-channel-manifest-schema*
*Context gathered: 2026-05-11*
