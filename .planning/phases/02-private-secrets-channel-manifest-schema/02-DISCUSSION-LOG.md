# Phase 2: Private secrets channel + manifest schema - Discussion Log

> **Audit trail only.** Do not use as input to planning, research, or execution agents.
> Decisions are captured in `02-CONTEXT.md` — this log preserves the alternatives considered.

**Date:** 2026-05-11
**Phase:** 02-private-secrets-channel-manifest-schema
**Mode:** `--auto` (recommended options auto-selected; user did not interactively answer)
**Areas discussed:** secrets-schema-shape, dto-shape, pat-build-storage, pat-injection-mechanism, secrets-url-discovery, dual-fetch-ordering-and-failure, bearer-auth-strategy, psk-masking, commit-strategy

---

## Private `secrets.json` schema shape

| Option | Description | Selected |
|--------|-------------|----------|
| Flat SSID→PSK map (`{ "Net": "PSK" }`) | Simplest; non-technical staff edit a 2-column file | ✓ |
| Mirror public manifest shape with PSKs | Parallel `wifi.profiles[]` array in the private file | |
| Wrapped envelope (`{ schemaVersion, wifiPsks: { ... } }`) | Forward-compat for non-Wi-Fi secret types | |

**Auto-selected:** Flat SSID→PSK map.
**Notes:** REQUIREMENTS.md WIFI-03 already specifies "keyed by Wi-Fi SSID → PSK"; flat map matches both the requirement text and the non-technical-author audience. Future non-Wi-Fi secret classes warrant a separate file, not envelope-multiplexing here.

---

## `WifiEntry` / `WifiProfileEntry` DTO shape

| Option | Description | Selected |
|--------|-------------|----------|
| Nested wrapper: `Manifest.Wifi: WifiEntry` → `Profiles: List<WifiProfileEntry>` | Matches existing `Cleanup` / `Wallpaper` / `Schedule` pattern | ✓ |
| Flat: `Manifest.Wifi: List<WifiProfileEntry>` | Skips the wrapper entirely | |

**Auto-selected:** Nested wrapper.
**Notes:** Codebase consistency wins. The wrapper leaves room for `wifi.enforceAll` / `wifi.deletePsksOnUninstall`-style policy fields without restructuring downstream consumers.

---

## `AuthMode` / `Scope` typing

| Option | Description | Selected |
|--------|-------------|----------|
| Strings with documented allowed values | Matches `ArchEntry.Kind`, `CommandEntry.Shell` | ✓ |
| Typed enums (`enum AuthMode { WPA2PSK, WPA3PSK, Open }`) | Compile-time validation | |

**Auto-selected:** Strings.
**Notes:** Consistent with existing schema fields. Runner in Phase 3 validates the value at use site.

---

## PAT build-time storage on the build box

| Option | Description | Selected |
|--------|-------------|----------|
| File-first, env-var fallback (`installer/secrets/wifi-pat.txt` → `$env:JAMINATOR_WIFI_PAT`) | Friendly to interactive builds today + future CI | ✓ |
| File only | Simpler but hostile to CI | |
| Env var only | Hostile to interactive Windows builds | |

**Auto-selected:** File-first, env-var fallback.
**Notes:** STATE.md already lists both as acceptable. Combined precedence matches `gh` / `aws` CLI credential resolution patterns.

---

## PAT injection mechanism into the EXE

| Option | Description | Selected |
|--------|-------------|----------|
| Generated `internal const string BuildSecrets.WifiPat` | Simplest call-site; const inlined into IL | ✓ |
| `[assembly: AssemblyMetadata("WifiPat", "...")]` | Reflective lookup at runtime | |
| `<EmbeddedResource>` read via `Assembly.GetManifestResourceStream` | Adds runtime parsing surface | |
| Companion file in MSI payload | Secret lives outside EXE — easier to recover | |

**Auto-selected:** Generated `internal const string`.
**Notes:** Success Criterion 5 says "implementation detail chosen during planning" — auto-mode picks `const string` for call-site simplicity. Threat model already accepts RE recovery (PROJECT.md Key Decisions), so the choice is about ergonomics, not security. Planner may refine if csproj globbing turns out non-trivial.

---

## Private repo URL discovery

| Option | Description | Selected |
|--------|-------------|----------|
| Baked into EXE at build time alongside PAT | URL and PAT rotate together | ✓ |
| Hardcoded constant in source | Forces source edits on repo migration | |
| Field in public `manifest.json` | Reveals private-repo existence in public manifest | |

**Auto-selected:** Baked into EXE at build time.
**Notes:** Pairs the endpoint with the credential it requires. Defense-in-depth: keeps the private repo URL off any public surface.

---

## Dual-fetch ordering

| Option | Description | Selected |
|--------|-------------|----------|
| Sequential, public-first then private | Explicit ordering; easy failure-mode reasoning | ✓ |
| Parallel `Task.WhenAll` | ~200ms saving on a working connection | |

**Auto-selected:** Sequential, public-first.
**Notes:** HARDEN-06 (M3) is the right place to redo this with the wallpaper fetch and other concurrent work in a unified parallel-I/O design.

---

## Private-fetch failure behaviour

| Option | Description | Selected |
|--------|-------------|----------|
| Fatal with joined-cache fallback | Loud failure on real config errors; cache covers offline-at-logon | ✓ |
| Non-fatal; proceed with empty `wifi.profiles` and warn | Permissive; risks silently leaving laptop on wrong network | |

**Auto-selected:** Fatal with joined-cache fallback.
**Notes:** "Wi-Fi access is login-safe" applies to runtime behaviour, not to silently masking a configuration error. A public profile with no matching private PSK is a real bug — surface it.

---

## Bearer-auth HttpClient strategy

| Option | Description | Selected |
|--------|-------------|----------|
| Per-request `HttpRequestMessage` on the existing static `HttpClient` | Local, explicit, no header leakage | ✓ |
| Second static `HttpClient` with `DefaultRequestHeaders.Authorization` | Simpler call site but global header is a footgun | |

**Auto-selected:** Per-request `HttpRequestMessage`.
**Notes:** Process-global default headers can accidentally be sent to unrelated URLs on misconfigured retries. Per-request is local and safe.

---

## PSK masking convention

| Option | Description | Selected |
|--------|-------------|----------|
| Fixed `***` | No length leakage; trivial to apply | ✓ |
| Length-preserving `********` | Leaks PSK length to anyone reading logs | |
| First-and-last char `p***d` | Worst of both — leaks boundary characters for marginal debug gain | |

**Auto-selected:** Fixed `***`.
**Notes:** Logs from school laptops may be sent to support over email/Slack. Length leakage gives a brute-force attacker a search-space hint.

---

## Commit strategy

| Option | Description | Selected |
|--------|-------------|----------|
| Atomic commits per file-area (5 commits, each builds cleanly) | Surgical revert path if Phase 3/4 smoke catches a regression | ✓ |
| One bundled commit | Single artifact, harder to bisect | |

**Auto-selected:** Atomic commits per file-area.
**Notes:** Mirrors Phase 1's D-11 commit pattern from M1, which proved its worth during the v0.7.5 ship.

---

## Claude's Discretion

- **`Jaminator.csproj` glob configuration** for `Generated/BuildSecrets.g.cs` — auto-glob vs explicit `<Compile Include>`. Planner picks after inspecting the csproj.
- **`BuildSecrets.g.cs` generation timing in `installer/build.ps1`** — generate from PowerShell before `dotnet build`, or wire as an MSBuild pre-build target. The minimal-change option is PS-first; planner has latitude.
- **Joined-cache atomicity mechanism** — two separate `.tmp + Move` pairs vs a single combined-JSON envelope file. Either satisfies "no half-write".
- **`secrets.json` deserialisation target type** — `Dictionary<string, string>` vs a typed `Secrets` wrapper class. Both fine; flat dictionary matches D-01.

## Deferred Ideas

### Phase 3
- `WifiProfileRunner` service + `netsh wlan add profile` invocation.
- "wifi" `SectionPanel` colour + `LoginSafeSections` membership.
- Profile-skip-with-warning when `Psk == null` after join.
- `WifiProfileEntry.ToString()` override (PSK masked) — actual override lives in Phase 3 where the runner will string-interpolate entries.

### Phase 4
- `netsh wlan show profile name=<SSID> key=clear` idempotency check.
- Per-Wi-Fi-failure `%TEMP%\Jaminator-wifi-error-*.log` (mirrors M1 DIAG-01).
- End-to-end smoke: clean → deploy → rotate PSK → re-deploy → idempotent third run.
- Non-technical-staffer PSK-rotation note in `docs/manifest-schema.md`.

### Phase 5
- Bump `Program.ToolVersion` to `"0.8.0"`, build production MSI, tag, GitHub Release.

### Milestone 3 (queued)
- **HARDEN-02** schema-version validation (high priority post-M2 because of the new `wifi` field).
- **HARDEN-06** parallel logon-path I/O (revisits the sequential dual-fetch chosen in D-09).
- **HARDEN-07** CI automation for PAT rotation + MSI rebuild.
- Cache-bundle envelope file as an alternative to two-file `.tmp + Move` (considered for D-10, deferred for co-design with HARDEN-06).
