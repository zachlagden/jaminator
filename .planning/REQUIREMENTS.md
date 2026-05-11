# Requirements: Jaminator — Milestone 2 (v0.8.0 — Wi-Fi password auto-deployment)

**Defined:** 2026-05-11
**Milestone version:** v0.8.0
**Core Value:** A technician can change behaviour on every school laptop by editing one JSON file in GitHub — no MSI redeploy, no per-machine login, no manual rollout.

**Milestone goal:** Extend the manifest-driven model to cover Wi-Fi profile deployment (including credentials), so adding or rotating a Wi-Fi network across the fleet is a single edit-and-push to the private manifest repo. **Hard constraint:** zero per-laptop touch after the initial MSI install — Jam Coding staff are not technicians.

## v1 Requirements

Requirements for the v0.8.0 release. Each maps to roadmap phases.

### WIFI — Wi-Fi profile deployment

- [ ] **WIFI-01**: Manifest schema additions — a new `wifi.profiles[]` array where each entry carries SSID, authentication mode (`WPA2PSK`, `WPA3PSK`, `open`), hidden flag, auto-connect flag, scope (`all-users` or `current-user`), and (in the **private** manifest only) the PSK. The schema is documented in `docs/manifest-schema.md` alongside the existing manifest entries.

- [ ] **WIFI-02**: A new `WifiProfileRunner` service (`src/Jaminator/Services/WifiProfileRunner.cs`) deploys each manifest-declared Wi-Fi profile to the laptop via `netsh wlan add profile filename=<xml-path> user=<scope>`. The XML is built per-profile at runtime from a template (similar to how `Installer.cs::RegisterScheduledTask` builds the task XML). Runner is invoked from the run-all path AND the login-mode path (Wi-Fi access is login-safe — it doesn't disrupt a logged-in student).

- [ ] **WIFI-03**: Wi-Fi profile passwords are delivered to fleet laptops via a **private GitHub manifest repo** gated by a fine-grained read-only PAT bundled in the MSI. Public `manifest/manifest.json` continues to live in the public `zachlagden/jaminator` repo and carries only non-sensitive config (program installs, cleanup rules, wallpaper, folder structure, commands, schedule, WIFI metadata WITHOUT PSKs). The **private** repo (e.g., `jamcoding-internal/jaminator-secrets`) carries a separate `secrets.json` (or `manifest-secrets.json`) keyed by Wi-Fi SSID → PSK. `ManifestFetcher` is extended to fetch the private secrets file in addition to the public manifest, using the bundled PAT as a bearer token. PSKs are joined into the in-memory profile entries at runtime. *Threat model is operational, not cryptographic — see PROJECT.md Key Decisions for the full rationale.*

- [ ] **WIFI-04**: Wi-Fi profile deployment is idempotent — `WifiProfileRunner` checks the existing profile (via `netsh wlan show profile name=<SSID>`) against the manifest-declared profile; if identical (SSID, auth mode, PSK, hidden, auto-connect, scope), the profile is skipped with a clean log message. Different settings trigger a delete-then-add to rewrite. Adopts the same skip-if-installed pattern as `MsiInstaller`.

- [ ] **WIFI-05**: Wi-Fi profile deployment failures (interface not present, PSK rejected by `netsh`, GPO override blocking write, profile XML invalid, scope rejected) are logged with actionable context (`Logger.Error` with full netsh stdout/stderr) and **do not abort** the rest of the run. Pattern matches the existing `CleanupRunner` and `CommandRunner` failure-isolation discipline. A new `Jaminator-wifi-error-YYYYMMDDhhmmss.log` is written to `%TEMP%` for the technician's diagnostic capture (same DIAG-01 pattern from M1).

## v2 Requirements

Acknowledged but deferred to future milestones. Tracked here so they're not lost.

### Milestone 3 candidates — Hardening + UX polish

- **HARDEN-01**: Code-signing / Authenticode verification of downloaded third-party MSI/EXE installers before execution (currently SHA256-only — manifest compromise would let an attacker swap hashes and binaries together)
- **HARDEN-02**: Manifest schema-version validation — reject manifests whose `schemaVersion` is newer than the tool understands, with a clear log message (currently fails silently to null fields). High priority post-M2 because M2 adds new manifest fields.
- **HARDEN-03**: Alternate manifest URL / fallback host — eliminate the single-point-of-failure on `raw.githubusercontent.com`
- **HARDEN-04**: TLS certificate pinning for `github.com` API endpoints, or at minimum structured logging of HTTPS failures to detect MITM
- **HARDEN-05**: CI / smoke-test automation for installer regressions — automated install on a clean Windows VM as part of every release
- **HARDEN-06**: Parallel logon-path I/O (manifest + secrets manifest + wallpaper fetched concurrently) to reduce student-visible wait at logon. Especially relevant post-M2 because we add a second network fetch per run.
- **HARDEN-07** (new from M2): **Automate the PAT-rotation + MSI-rebuild pipeline**. Termly rotation of the embedded PAT requires manual MSI rebuild + release today. A small CI workflow that triggers on a `secrets-rotation/` repo event would do it autonomously and shrink the rotation window.
- **UX-01**: UX/UI polish across the WinForms section panels and progress display (described as "light" — code and UI already look decent)

## M1 carry-forward

- **INSTALL-02** (from M1, v0.7.5): Boss confirms double-click MSI install succeeds on a Win10 school-target laptop. *Not in M2 scope — tracked separately as the gate to formally close out M1. SelfUpdater on every existing fleet install will auto-upgrade to v0.7.5 on next launch, so this is real-world-implicitly happening regardless of explicit boss confirmation.*

## Out of Scope

Explicitly excluded from Milestone 2. Documented to prevent scope creep.

| Feature | Reason |
|---------|--------|
| Encrypted-at-rest PSK in the public manifest | Considered and explicitly rejected. Every variant is rot13 once the public MSI ships (the decryption key would have to be in the MSI, which is publicly downloadable). See PROJECT.md Key Decisions. WIFI-03 chooses operational security (private-repo + PAT) over cryptographic-theatre. |
| WPA2-Enterprise / 802.1X / per-device certs | The "real enterprise" answer for Wi-Fi security. Requires a RADIUS server and per-device certificate enrollment. Out of scope for a school fleet without that infrastructure; revisit if Jam Coding ever has the RADIUS infra. |
| Wi-Fi profile name aliasing or templating | Manifest entries are 1:1 with deployed profiles. No "deploy this profile to schools in group A only" logic. If needed later, lives in M3 or M4. |
| Storing past PSK versions for rollback | The manifest carries the current desired state; if a rotation goes wrong, edit the manifest back. No git-style snapshot of profile history in Jaminator itself (git history of the private manifest repo provides this for free). |
| Hardening pass + UX/UI polish | Planned as Milestone 3 — sequencing decision: feature before hardening. |
| CI / automated installer regression testing | Listed as HARDEN-05 for Milestone 3 |
| Automating PAT rotation | Listed as HARDEN-07 for Milestone 3 (new from M2) — for v0.8.0 the rotation is manual, same as any other MSI release |

## Traceability

Which phases cover which requirements.

| Requirement | Phase | Status |
|-------------|-------|--------|
| WIFI-01 | Phase 2 — Private secrets channel + manifest schema | Pending |
| WIFI-02 | Phase 3 — WifiProfileRunner + run-path integration | Pending |
| WIFI-03 | Phase 2 — Private secrets channel + manifest schema | Pending |
| WIFI-04 | Phase 4 — Idempotency, failure isolation, smoke test | Pending |
| WIFI-05 | Phase 3 (partial) + Phase 4 (full discipline) | Pending |

**Coverage:**
- v1 requirements: 5 total
- Mapped to phases: 5 ✓
- Unmapped: 0
- Phase 5 (ship v0.8.0) carries no v1 WIFI-* directly; it ships the work the WIFI-* phases produced.

---
*Requirements defined: 2026-05-11*
*Last updated: 2026-05-11 after roadmap creation — 5 WIFI requirements mapped across Phases 2–4; Phase 5 is the release phase*
