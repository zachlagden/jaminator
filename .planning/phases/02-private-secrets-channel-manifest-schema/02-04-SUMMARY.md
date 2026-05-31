---
phase: 02-private-secrets-channel-manifest-schema
plan: 04
subsystem: docs
tags:
  - docs
  - manifest-schema
  - wifi
  - secrets
  - threat-model
  - WIFI-01
  - WIFI-03
requires: []
provides:
  - "Public wifi.profiles[] schema documented for technician authors"
  - "Private secrets.json schema + GitHub REST contents-endpoint fetch contract documented for ManifestFetcher implementer (plan 02-03)"
  - "Cache topology contract (atomic-pair write under %ProgramData%\\Jaminator\\cache\\) documented for code plans"
  - "Threat-model honesty note preserved on the public doc surface (locks WIFI-03 design intent against future re-litigation)"
affects:
  - docs/manifest-schema.md
tech-stack:
  added: []
  patterns:
    - "Markdown H2 sections appended after `cleanup`, matching existing doc voice and heading levels"
    - "GitHub REST API contents endpoint (api.github.com/repos/{owner}/{repo}/contents/{path}?ref={branch}) with Bearer auth and Accept: application/vnd.github.raw+json — locked as the canonical private-fetch shape"
    - "raw.githubusercontent.com explicitly named as anti-pattern to prevent future 'simplification'"
key-files:
  created:
    - .planning/phases/02-private-secrets-channel-manifest-schema/02-04-SUMMARY.md
  modified:
    - docs/manifest-schema.md
decisions:
  - "Documented D-01 (flat SSID→PSK secrets.json — no envelope, no schemaVersion)"
  - "Documented D-03 (WifiProfileEntry six-field shape + allowed AuthMode/Scope string values + nullable PSK)"
  - "Documented D-08 (private secrets URL is build-time-baked, not in public manifest)"
  - "Documented D-10 (atomic-pair cache topology with candid between-two-Moves residual)"
  - "Documented D-15 (PSK masking as `***` in every log emission path)"
  - "Quoted WIFI-03 Key Decisions rationale verbatim — operational opacity not cryptographic protection, encryption-at-rest rejected as rot13"
metrics:
  duration_min: 2
  completed: "2026-05-31T15:17:30Z"
---

# Phase 2 Plan 4: Manifest schema docs (Wi-Fi + private secrets channel + threat model) Summary

## One-liner

Appended four new H2 sections to `docs/manifest-schema.md` — `wifi`, `Private secrets channel`, `Cache topology`, and `Threat model — operational, not cryptographic` — documenting the WIFI-01 / WIFI-03 surface introduced in Phase 2 without modifying any existing 182 lines.

## What changed

`docs/manifest-schema.md` grew by 113 lines. The existing top-level header, top-level fields table, and the H2 sections for `wallpaper`, `folders`, `programs`, `commands`, `cleanup` are byte-identical (verified via `diff` against `HEAD~1`). The four new sections appended in this order:

1. **`## \`wifi\``** — Six-field schema for `wifi.profiles[]`: `ssid` (string, required), `authMode` (`"WPA2PSK"` | `"WPA3PSK"` | `"open"`, default `"WPA2PSK"`), `hidden` (bool, default `false`), `autoConnect` (bool, default `true`), `scope` (`"all-users"` | `"current-user"`, default `"all-users"`), `psk` (string, never in public manifest). Includes a JSON example, a markdown field table matching the existing top-level-fields style, and an explicit callout that PSKs never appear in the public manifest.

2. **`## Private secrets channel`** — Three H3 sub-sections: **Schema** (flat `{ "SSID": "PSK" }` map example, no envelope), **Fetch mechanism** (GitHub REST API contents endpoint with Bearer auth and `Accept: application/vnd.github.raw+json` + `User-Agent` + `X-GitHub-Api-Version: 2026-03-10`), **PAT scope** (fine-grained PAT, `Contents: Read` on private repo only, no org-level permissions, no other repos selected), and **Operator workflow** (cross-link to `installer/secrets/README.md`). Includes the explicit anti-pattern callout: **Do NOT use `raw.githubusercontent.com`** — it silently ignores `Authorization` for private repos.

3. **`## Cache topology`** — Documents the two-file pair under `%ProgramData%\Jaminator\cache\` (`manifest.json` + `secrets.json`), the atomic-pair-write semantics, and the three failure modes (both-online-succeed / either-fails-with-cache / either-fails-without-cache → `InvalidOperationException`). Includes the candid between-two-`File.Move` rare-failure window and notes HARDEN-06 (M3 parallel logon-path I/O) as the planned revisit.

4. **`## Threat model — operational, not cryptographic`** — Quotes PROJECT.md WIFI-03 rationale verbatim as a blockquote: "operational opacity, not cryptographic protection"; in-scope / out-of-scope / mitigation breakdown; encryption-at-rest considered and **rejected** as rot13. Names the Windows-level `netsh wlan show profile name=<SSID> key=clear` reality explicitly so future maintainers don't claim guarantees the design doesn't deliver. Documents the `***` PSK masking convention enforced at the log emission site.

## Tasks completed

| Task | Name | Commit | Files |
|------|------|--------|-------|
| 1 | Add four new sections to docs/manifest-schema.md | `d50a66c` | `docs/manifest-schema.md` |

## Verification

All 14 acceptance criteria in the plan pass:

- #1: All four H2 headings present (`## \`wifi\``, `## Private secrets channel`, `## Cache topology`, `## Threat model — operational, not cryptographic`)
- #2: All six WifiProfileEntry fields documented (`ssid`, `authMode`, `hidden`, `autoConnect`, `scope`, `psk`)
- #3: All three `authMode` enum values listed (`WPA2PSK`, `WPA3PSK`, `open`)
- #4: Both `scope` enum values listed (`all-users`, `current-user`)
- #5: GitHub REST API endpoint form documented (`api.github.com/repos`, `application/vnd.github.raw+json`, `X-GitHub-Api-Version`)
- #6: `raw.githubusercontent.com` anti-pattern explicitly named with "silently ignores" callout
- #7: Fine-grained PAT scope documented (`Contents: Read`)
- #8: Cache topology names both files, `%ProgramData%`, and the atomic-pair semantics
- #9: Threat-model section is candid — `operational opacity`, `reverse-engineer` / `recover the PAT` / `rot13`, `termly` / `rotation`, `WIFI-03` cross-reference, and the `netsh wlan show profile key=clear` Windows reality
- #10: `installer/secrets/README.md` cross-link present
- #11: `***` PSK mask literal present
- #12: Negative checks — `WifiProfileRunner` does NOT appear (only the explicit forward-pointer `netsh wlan add profile (Phase 3 — out of scope for this doc's depth)` which the plan explicitly permits); `HARDEN-07` does NOT appear; no real PAT value matching `github_pat_[A-Za-z0-9_]{20,}` appears
- #13: Existing 182 lines are byte-identical (`diff HEAD~1 docs/manifest-schema.md` against `head -182` of new file confirms no changes to existing content)
- #14: Exactly one `# Manifest schema` H1

## Deviations from Plan

None — plan executed exactly as written. The action block in Task 1 was self-contained and the content boundaries (what NOT to do) were respected verbatim.

## Known Stubs

None — this is a documentation plan with no code stubs. The doc references three pieces of work that have not yet shipped (and are explicitly marked as such in the doc text):

- `installer/secrets/README.md` operator runbook — created by plan 02-02 (separate worktree, Wave 1)
- `ManifestFetcher` private-fetch path — implemented by plan 02-03 (Wave 2)
- Phase 3 `netsh wlan add profile` deployment — out of scope for this doc's depth, named only as a one-liner forward-pointer

These are not stubs in the "deferred functionality" sense — they are forward-references to planned, sequenced work in the phase plan. The docs side of WIFI-01 / WIFI-03 (the entire scope of this plan) is complete.

## Threat Flags

None — this plan introduces no new code surface. The four new doc sections document existing design decisions and explicitly mitigate two doc-surface threats already enumerated in the plan's threat register:

- **T-04 (doc surface)** — A future maintainer re-introducing `raw.githubusercontent.com` for the private fetch. **Mitigated:** explicit "Do NOT use" callout naming the host and the reason (silent `Authorization` header drop for private repos).
- **T-01 (doc surface)** — A real PAT or PSK getting committed as an example value. **Mitigated:** placeholder strings only (`"<the PSK for SchoolNet-Year3>"`, `"github_pat_…redacted…"`); the negative grep gate in acceptance #12 (`! grep -E 'github_pat_[A-Za-z0-9_]{20,}'`) returns 0 matches.
- **Threat-model softening** — A future PR rewriting the threat-model section to overstate guarantees. **Mitigated:** the threat-model section blockquotes PROJECT.md WIFI-03's rationale verbatim and includes the candid `netsh wlan show profile key=clear` reality; acceptance #9 grep-gates the specific phrases.
- **Spurious feature documentation** — Documenting Phase 3/4 features as if they're shipped in Phase 2. **Mitigated:** `WifiProfileRunner` and similar Phase 3 identifiers are NOT used as anything other than the explicit `Phase 3 — out of scope for this doc's depth` forward-pointer; acceptance #12 grep gate confirms.

No new threat-model surface beyond what the plan's `<threat_model>` already enumerated. No code paths, no new network endpoints, no auth code, no schema changes at trust boundaries (the schema itself is described, not implemented).

## Self-Check: PASSED

Verified the following before writing this section:

- **Files exist:**
  - `docs/manifest-schema.md` — FOUND (modified, was 182 lines, now 295 lines)
  - `.planning/phases/02-private-secrets-channel-manifest-schema/02-04-SUMMARY.md` — FOUND (this file)
- **Commits exist:**
  - `d50a66c` (Task 1) — FOUND in `git log` on `worktree-agent-a18f0e551e33b9981`
- **Branch posture:** HEAD on `worktree-agent-a18f0e551e33b9981`, base at `85f8f8c` (Phase 2 start). No drift, no commits to protected refs, no clean/reset operations performed.
- **Acceptance criteria:** All 14 gates from the plan pass (recorded verbatim in `## Verification` above).
- **Byte-identity of existing content:** `diff` of `git show HEAD~1:docs/manifest-schema.md` against `head -182 docs/manifest-schema.md` returns empty — existing 182 lines unchanged.
