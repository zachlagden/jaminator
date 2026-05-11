# Roadmap: Jaminator — Milestone 2 (Wi-Fi password auto-deployment, v0.8.0)

## Overview

v0.7.4 / v0.7.5 give Jam Coding a manifest-driven fleet maintenance tool — but Wi-Fi credentials aren't in the manifest, so when a school SSID/PSK rotates, the only path today is per-laptop intervention. Milestone 2 extends the manifest model to cover Wi-Fi profile deployment so that adding or rotating a network is one edit-and-push, with **zero per-laptop touch** after the existing MSI install (Jam Coding staff are not technicians).

The credential-storage design is **locked** (PROJECT.md Key Decisions, REQUIREMENTS.md WIFI-03): the public `zachlagden/jaminator` manifest stays public and stays PSK-free; a new private GitHub repo (e.g. `jamcoding-internal/jaminator-secrets`) carries the SSID→PSK map; the MSI bundles a fine-grained read-only PAT for that private repo; `ManifestFetcher` joins the two at runtime in memory. The threat model is operational (keep PSKs off the search-indexable internet), not cryptographic (any MSI RE recovers the PAT — accepted, mitigated by termly rotation aligned with PSK rotation). This is **not revisited** in the roadmap.

Four phases ship v0.8.0. Each phase delivers an end-to-end-demonstrable slice (MVP mode): no phase ends with "the code compiles" — phases end with "a laptop is on the intended Wi-Fi network." The verification bar matches M1: manual smoke-test on a clean Win11 box, no automated test scaffolding (HARDEN-05 is queued for M3). Goal-backward at milestone end: a non-technical Jam Coding staffer can add a new Wi-Fi network by editing one file in the private secrets repo and pushing — every fleet laptop applies it on the next manifest fetch (login-mode or run-all), and the PSK never appears on a public/search-indexable channel.

## Phases

**Phase Numbering:**
- Integer phases (2, 3, 4, 5): Planned milestone work (M1 used phase 1; M2 continues numbering)
- Decimal phases (2.1, 2.2): Urgent insertions (marked with INSERTED)

Decimal phases appear between their surrounding integers in numeric order.

- [ ] **Phase 2: Private secrets channel + manifest schema** - Stand up the private secrets repo, extend `Manifest.cs` DTOs for `wifi.profiles[]`, extend `ManifestFetcher` to dual-fetch (public manifest + private secrets) with bearer-token auth, and bake a build-time PAT into the binary via `build.ps1`. End-state: a debug build run on the dev box logs a fully-joined manifest with at least one Wi-Fi profile + PSK loaded from the private channel.
- [ ] **Phase 3: WifiProfileRunner service + run-path integration** - New `WifiProfileRunner` service builds the `netsh wlan add profile` XML per entry and applies it; wired into the run-all path, the login-mode path, and the WinForms UI as a new "wifi" section card. End-state: running Jaminator on the dev laptop actually puts the laptop onto a test SSID configured via the private secrets repo.
- [ ] **Phase 4: Idempotency, failure isolation, and end-to-end smoke test** - Diff-and-skip via `netsh wlan show profile`, delete-then-add on settings drift, fail-open with actionable `%TEMP%` diagnostic logs on netsh/PSK/GPO failures, and a documented end-to-end smoke test (deploy → rotate PSK → re-deploy → verify) on the dev laptop. End-state: the dev-laptop smoke test passes; rotating a PSK in the private repo and re-running Jaminator re-associates the laptop to the new credential cleanly.
- [ ] **Phase 5: Tag and ship v0.8.0** - Version bump in `Program.cs`, MSI build with the production PAT injected at build time, git tag, GitHub Release with MSI asset and release notes that explain the new manifest fields, the private-secrets workflow, and the PAT-rotation operator procedure.

## Phase Details

### Phase 2: Private secrets channel + manifest schema
**Goal**: Jaminator can fetch and deserialise a complete fleet config from two sources — public manifest (no PSKs) and private secrets repo (PSKs) — joined in memory at startup, with the private-repo PAT baked into the binary at build time and never committed to source control.
**Mode:** mvp
**Depends on**: Nothing (first M2 phase; builds on v0.7.5 baseline)
**Requirements**: WIFI-01, WIFI-03
**Success Criteria** (what must be TRUE):
  1. A private GitHub repo exists (e.g. `jamcoding-internal/jaminator-secrets`) containing a `secrets.json` schema-documented in `docs/manifest-schema.md`, with at least one real SSID→PSK entry for the dev-laptop test network
  2. A fine-grained read-only PAT scoped to that repo is generated and stored locally in a `.gitignore`'d file (e.g. `installer/secrets/wifi-pat.txt`) or local env var — and is verifiably absent from `git status` and `git log` after the phase completes
  3. `src/Jaminator/Models/Manifest.cs` defines a `WifiEntry` + `WifiProfileEntry` DTO matching the WIFI-01 schema (SSID, auth mode, hidden, autoConnect, scope, PSK), wired into the top-level `Manifest` class as `wifi`
  4. `src/Jaminator/Services/ManifestFetcher.FetchAsync` performs the dual fetch (public manifest + private secrets), joins SSID→PSK into the in-memory `WifiProfileEntry` list, caches both alongside the existing `manifest.json` cache under `%ProgramData%\Jaminator\cache\`, and falls back to the joined cached pair when offline (login-mode resilience preserved)
  5. `installer/build.ps1` reads the PAT from the local-only source at build time, injects it into the EXE (embedded resource OR `[assembly:]` constant via msbuild property OR companion file written into the MSI payload — implementation detail chosen during planning), and the resulting `Jaminator.exe` / `Jaminator.msi` deserialises and joins the private secrets when launched on the dev box (verified by a debug log line showing the joined profile count and SSID list, PSKs masked)
**Plans**: TBD

### Phase 3: WifiProfileRunner service + run-path integration
**Goal**: Every manifest-declared Wi-Fi profile is actually deployed onto the laptop via `netsh wlan add profile`, on both the interactive run-all path and the silent login-mode path, with a visible "wifi" section card in the UI matching the existing section pattern.
**Mode:** mvp
**Depends on**: Phase 2
**Requirements**: WIFI-02, WIFI-05 (partial — full failure-isolation polish lands in Phase 4)
**Success Criteria** (what must be TRUE):
  1. `src/Jaminator/Services/WifiProfileRunner.cs` exists, accepts a `Logger` + `List<WifiProfileEntry>`, builds the per-profile `netsh wlan add profile` XML at runtime from a template (modelled on `Installer.RegisterScheduledTask`'s XML-build pattern), writes the XML to a `%TEMP%` path with restrictive ACLs, invokes `netsh wlan add profile filename=<path> user=<scope>`, and deletes the XML on success or failure
  2. `MainForm` renders a "wifi" `SectionPanel` (colour-coded distinct from existing sections per the conventions in `MainForm.cs`), and `LoginSafeSections` includes `"wifi"` so the login-mode scheduled task applies Wi-Fi profiles at every logon
  3. Running `Jaminator.exe --run-all` on the dev laptop deploys the test Wi-Fi profile and the laptop appears in `netsh wlan show profiles` with the manifest-declared SSID, auth mode, and scope
  4. Running `Jaminator.exe --login-mode` (or invoking the `Jaminator-Login` scheduled task) on the dev laptop with no UI visible deploys the same profile silently and logs success to the daily logfile under `%ProgramData%\Jaminator\logs\`
  5. After a fresh OS reboot, the dev laptop auto-associates to the test SSID without manual intervention (validates `autoConnect` + scope + `connectionMode=auto` plumb-through into the netsh XML)
**Plans**: TBD

### Phase 4: Idempotency, failure isolation, and end-to-end smoke test
**Goal**: The Wi-Fi runner is safe to invoke on every login forever — it skips clean when nothing has changed, rewrites cleanly when settings drift, never aborts the rest of the run on failure, leaves a per-failure diagnostic log in `%TEMP%` matching the DIAG-01 pattern from M1, and is proven end-to-end on the dev laptop including a PSK rotation cycle.
**Mode:** mvp
**Depends on**: Phase 3
**Requirements**: WIFI-04, WIFI-05 (full failure-isolation discipline)
**Success Criteria** (what must be TRUE):
  1. `WifiProfileRunner` reads the existing profile via `netsh wlan show profile name=<SSID> key=clear` (where permitted) and skips with an `Info`-level "already up to date" log when SSID + auth mode + PSK + hidden + autoConnect + scope all match the manifest entry; settings drift triggers a `netsh wlan delete profile` followed by add (rewrite path)
  2. Failures (interface absent, PSK rejected by `netsh`, GPO override, XML invalid, scope rejected) are caught per-profile, logged via `Logger.Error` with full netsh stdout + stderr, and the runner continues to the next profile and the next section — matching the `CleanupRunner` / `CommandRunner` failure-isolation pattern; the overall run exit code reflects "any section failed" without aborting mid-run
  3. On any Wi-Fi failure, a `Jaminator-wifi-error-YYYYMMDDhhmmss.log` is written to `%TEMP%` capturing the offending SSID (PSK masked), the full netsh invocation, and the captured stdout/stderr — matching the M1 DIAG-01 triple-channel pattern from `installer/UpdateCheck` removal work
  4. End-to-end smoke test passes on the dev Win11 laptop: (a) start in a state where the test SSID is NOT present in `netsh wlan show profiles`; (b) run Jaminator; (c) confirm the laptop is associated to the test SSID; (d) edit the PSK in the private secrets repo, push; (e) re-run Jaminator; (f) confirm the laptop re-associates using the new PSK; (g) run Jaminator a third time with no manifest changes and confirm the runner logs "skipped — already up to date" for every profile
  5. `docs/manifest-schema.md` is updated with the full `wifi.profiles[]` schema, the private-secrets `secrets.json` schema, and a short "how to rotate a Wi-Fi password across the fleet" operator note targeting the non-technical Jam Coding staff audience
**Plans**: TBD

### Phase 5: Tag and ship v0.8.0
**Goal**: v0.8.0 is published as a tagged GitHub Release with the rebuilt MSI (PAT injected from the production secret at build time) attached, release notes that explain the new capability + new manifest fields + the PAT-rotation operator workflow, and an updated PROJECT.md / REQUIREMENTS.md reflecting milestone closure.
**Mode:** mvp
**Depends on**: Phase 4
**Requirements**: (no v1 WIFI-* directly — this phase ships the work the WIFI-* phases produced)
**Success Criteria** (what must be TRUE):
  1. `Program.cs::ToolVersion` reads `"0.8.0"` (the single source of truth that `installer/build.ps1` parses for the MSI version)
  2. `installer/build.ps1` runs on the Windows build box with the production PAT in the local-only secret file/env var and produces a `build/Jaminator.msi` that, on a fresh Win11 install, deserialises the production private secrets and deploys the production Wi-Fi profile(s)
  3. A `v0.8.0` annotated tag exists in the git repo, pushed to origin; a GitHub Release named `v0.8.0` exists at `github.com/zachlagden/jaminator/releases/tag/v0.8.0` with `Jaminator.msi` attached as a downloadable asset
  4. Release notes body explains: (a) the new capability (manifest-driven Wi-Fi profile deploy with zero per-laptop touch), (b) the new public-manifest `wifi.profiles[]` schema, (c) the private-secrets repo workflow for adding/rotating PSKs (target audience: non-technical Jam Coding staff), (d) the PAT-rotation operator procedure (when to rotate, how to regenerate, how to rebuild and re-ship the MSI), and (e) the documented threat-model limits (RE-recoverable PAT, `netsh wlan show profile key=clear` exposure) so the operator knows what this design does and does not promise
  5. SelfUpdater on every existing v0.7.5 fleet install auto-upgrades to v0.8.0 on next launch (validates the M1 SelfUpdater chain still works across the v0.7.5 → v0.8.0 upgrade step — no new code, but a real-world smoke-test of the MSI ships-cleanly path)
**Plans**: TBD

## Progress

**Execution Order:**
Phases execute in numeric order: 2 → 3 → 4 → 5

| Phase | Plans Complete | Status | Completed |
|-------|----------------|--------|-----------|
| 2. Private secrets channel + manifest schema | 0/TBD | Not started | - |
| 3. WifiProfileRunner + run-path integration | 0/TBD | Not started | - |
| 4. Idempotency, failure isolation, smoke test | 0/TBD | Not started | - |
| 5. Tag and ship v0.8.0 | 0/TBD | Not started | - |
