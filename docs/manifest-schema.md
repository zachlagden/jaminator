# Manifest schema

The manifest at `manifest/manifest.json` is the source of truth. The Jaminator EXE fetches it from `https://raw.githubusercontent.com/zachlagden/jaminator/main/manifest/manifest.json` on every launch.

## Top-level fields

```json
{
  "schemaVersion": 1,
  "manifestVersion": "2026.05.06.1",
  "minimumToolVersion": "0.1.0",
  "wallpaper": { ... },
  "folders": [ ... ],
  "programs": [ ... ],
  "commands": [ ... ],
  "cleanup": { ... }
}
```

| Field | Purpose |
|-------|---------|
| `schemaVersion` | Bumped only on breaking schema changes. Tool refuses unknown versions. |
| `manifestVersion` | Free-form string, shown in UI so techs can confirm what's running. Convention: `YYYY.MM.DD.N`. |
| `minimumToolVersion` | If the tool is older than this, it refuses to run and prompts self-update. |

## `wallpaper`

```json
"wallpaper": {
  "url": "https://raw.githubusercontent.com/zachlagden/jaminator/main/assets/wallpaper.png",
  "sha256": "abc123...",
  "enforce": true
}
```

`enforce: true` means cleanup will revert the wallpaper to this if the user changed it.

## `folders`

```json
"folders": [
  { "path": "Documents/St Mary's", "createIfMissing": true },
  { "path": "Documents/Westwood Primary", "createIfMissing": true }
]
```

`path` is relative to the current user's profile (`%USERPROFILE%`). Forward slashes are fine.

## `programs`

Each program has per-architecture installer entries. The tool detects 32 vs 64-bit Windows and picks the matching one (falls back to whatever arch is provided if only one is set).

### `kind`

Three installer kinds are supported:

- `msi` — standard Windows Installer. Runs `msiexec /i <file> <args>`.
- `exe` — generic installer EXE (NSIS, InstallShield, custom). Runs the EXE with `<args>`.
- `zip-extract` — portable app bundled as a zip. Extracts to `installPath`, optionally creates desktop / start-menu shortcuts pointing at `exeName`.

### MSI example

```json
{
  "id": "kodu",
  "name": "Kodu Game Lab",
  "x86": {
    "kind": "msi",
    "url": "https://github.com/zachlagden/jaminator/releases/download/installers-v1/KoduSetup_1.6.18.0.msi",
    "sha256": "...",
    "args": "/qn /norestart ALLUSERS=1",
    "prerequisites": [
      {
        "kind": "msi",
        "url": "https://github.com/zachlagden/jaminator/releases/download/installers-v1/xnafx40_redist.msi",
        "sha256": "...",
        "args": "/qn /norestart"
      }
    ]
  },
  "detect": {
    "registryKey": "HKLM\\SOFTWARE\\WOW6432Node\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\Kodu Game Lab"
  }
}
```

`prerequisites` are installed in order before the main installer (e.g. XNA Framework before Kodu).

### EXE example

```json
{
  "id": "scratch-desktop",
  "name": "Scratch Desktop",
  "x86": {
    "kind": "exe",
    "url": "https://...Scratch Desktop Setup 3.9.0.exe",
    "sha256": "...",
    "args": "/S /allusers"
  },
  "detect": {
    "registryKey": "HKLM\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\Scratch Desktop"
  }
}
```

### zip-extract example (portable app)

```json
{
  "id": "pivot-animator",
  "name": "Pivot Animator v5",
  "x86": {
    "kind": "zip-extract",
    "url": "https://...pivot-v5.zip",
    "sha256": "...",
    "installPath": "%ProgramFiles(x86)%\\Pivot Animator v5",
    "exeName": "pivot.exe",
    "shortcutName": "Pivot Animator v5",
    "desktopShortcut": true,
    "startMenuShortcut": true
  },
  "detect": {
    "filePath": "%ProgramFiles(x86)%\\Pivot Animator v5\\pivot.exe"
  }
}
```

### Detection rules

The `detect` block determines whether the install can be skipped. Any field present is checked:

- `filePath` — file exists at the expanded path
- `registryKey` — uninstall key exists; `minVersion` (optional) compared against `DisplayVersion`
- `appxPackageName` — Appx package present (for Microsoft Store apps like Minecraft Education)

If any rule matches, the program is considered installed and skipped.

A 404 on the installer URL fails just that one program — other programs in the list still run.

## `commands`

Arbitrary PowerShell. Runs as administrator. Each command is shown in the UI by `name` so the tech can choose what to run.

```json
{
  "id": "disable-cortana",
  "name": "Disable Cortana",
  "shell": "powershell",
  "script": "New-Item 'HKLM:\\SOFTWARE\\Policies\\Microsoft\\Windows\\Windows Search' -Force | Out-Null; Set-ItemProperty 'HKLM:\\SOFTWARE\\Policies\\Microsoft\\Windows\\Windows Search' -Name AllowCortana -Value 0 -Type DWord"
}
```

`shell` values: `powershell` (Windows PowerShell 5.1) or `cmd`.

## `cleanup`

```json
"cleanup": {
  "tempPaths": [
    "%TEMP%",
    "%WINDIR%\\Temp",
    "%WINDIR%\\Prefetch",
    "%LOCALAPPDATA%\\Microsoft\\Windows\\INetCache"
  ],
  "emptyRecycleBin": true,
  "clearBrowserCache": {
    "edge": true,
    "chrome": true,
    "firefox": true
  },
  "documentsAllowlist": {
    "enabled": true,
    "quarantineFolder": "Documents/_unsorted",
    "allowedSubfolders": ["St Mary's", "Westwood Primary"],
    "allowedFiles": ["desktop.ini"]
  },
  "resetWallpaperIfChanged": true
}
```

`documentsAllowlist` moves any unexpected files in `Documents/` into a quarantine folder rather than deleting — so kids' work isn't lost. The allowlist is computed from `folders` + `allowedSubfolders` + `allowedFiles`.

## `wifi`

Wi-Fi profiles deploy via `netsh wlan add profile` (Phase 3 — out of scope for this doc's depth) on every laptop the EXE runs on. The public manifest carries everything **except** the PSK; the PSK is delivered separately via the [private secrets channel](#private-secrets-channel) (see below). Authoring a `wifi` block is optional — manifests with no `wifi` field continue to work unchanged.

```json
"wifi": {
  "profiles": [
    {
      "ssid": "SchoolNet-Year3",
      "authMode": "WPA2PSK",
      "hidden": false,
      "autoConnect": true,
      "scope": "all-users"
    }
  ]
}
```

| Field | Type | Default | Notes |
|-------|------|---------|-------|
| `ssid` | string | (required) | SSID broadcast by the access point. Case-sensitive — must match exactly what `netsh wlan show networks` reports. |
| `authMode` | string | `"WPA2PSK"` | Allowed values: `"WPA2PSK"`, `"WPA3PSK"`, `"open"`. String-typed for authoring forgiveness; the Phase 3 runner validates at deploy site. |
| `hidden` | bool | `false` | Set `true` for non-broadcasting SSIDs (the laptop probes for them explicitly). |
| `autoConnect` | bool | `true` | Whether Windows should auto-associate to this SSID when in range. |
| `scope` | string | `"all-users"` | Allowed values: `"all-users"` (fleet-wide profile, the typical case), `"current-user"` (profile visible only to the user who triggered the install). |
| `psk` | string | (never in public manifest) | Pre-shared key. **Never set this field in the public manifest.** It is populated in memory at runtime from the private secrets channel (see below). |

> **PSKs never appear in the public manifest.** The `psk` field is reserved for runtime population from the private secrets channel. If you find yourself writing a PSK into `manifest/manifest.json`, stop — the public manifest lives on `github.com/zachlagden/jaminator` and is indexed by search engines.

## Private secrets channel

PSKs live in a **separate, private GitHub repo** gated by a fine-grained read-only Personal Access Token (PAT) bundled in the MSI. `ManifestFetcher` fetches the public manifest as today, then fetches the private `secrets.json` using the bundled PAT as a Bearer token, then joins SSID→PSK in memory before returning the `Manifest` to the rest of the application. The private URL is **baked into the binary at build time** by `installer/build.ps1` — it is not present in the public manifest, and it is not configurable at runtime.

### Schema (`secrets.json`)

A flat JSON object. Keys are SSIDs (case-sensitive — must match the public manifest's `ssid` field exactly), values are PSKs as strings:

```json
{
  "SchoolNet-Year3": "<the PSK for SchoolNet-Year3>",
  "SchoolNet-Staff": "<the PSK for SchoolNet-Staff>"
}
```

No envelope, no `schemaVersion` field, no nested structure. Adding a new SSID is one line. An SSID that appears in the public `wifi.profiles[]` list but is missing from `secrets.json` is reported at runtime as an unjoinable profile (logged, the rest of the run proceeds).

### Fetch mechanism

The private `secrets.json` MUST be fetched via the GitHub REST API contents endpoint with Bearer auth and the raw-bytes media type:

```http
GET https://api.github.com/repos/{owner}/{repo}/contents/{path}?ref={branch}
Authorization: Bearer <fine-grained-PAT>
Accept: application/vnd.github.raw+json
User-Agent: Jaminator/<version>
X-GitHub-Api-Version: 2026-03-10
```

`Accept: application/vnd.github.raw+json` causes the API to return the raw file bytes (the JSON itself), not the wrapping metadata envelope. The `User-Agent` header is required by GitHub's API; without it requests are rejected. `X-GitHub-Api-Version` pins the API version so a future GitHub change cannot silently alter the response shape.

> **Do NOT use `raw.githubusercontent.com`** for the private fetch. It silently ignores the `Authorization` header for private repos — the request will 404 with no indication that auth was even tried, or worse, return a 200 from a public fork that happens to share the name. The REST API contents endpoint at `api.github.com` is the only host that honours Bearer auth for private content. See the inline comment in `src/Jaminator/Services/ManifestFetcher.cs` for the canonical fetcher — do not "simplify" it back to a raw URL.

### PAT scope

Generate a **fine-grained** PAT (NOT a classic PAT) with these permissions:

- **Contents: Read** on the private secrets repo ONLY (not "All repositories")
- **Metadata: Read** is auto-included by GitHub when any other repo permission is granted
- No org-level permissions
- No other repos selected

A placeholder for documentation purposes only looks like `"github_pat_…redacted…"` — real PAT values must never appear in this doc, in the public manifest, or in any committed file.

### Operator workflow

See [`installer/secrets/README.md`](../installer/secrets/README.md) for the build-operator runbook: where to drop the PAT on the build box, the env-var fallback (`JAMINATOR_WIFI_PAT`), how `installer/build.ps1` consumes it, and the termly rotation cadence aligned with PSK rotation.

## Cache topology

Both the public manifest and the private `secrets.json` are cached under `%ProgramData%\Jaminator\cache\` so logon-time runs survive offline classroom periods. The two files are written **atomically as a pair**: both `.tmp` files are written first, then `File.Delete` + `File.Move` is applied per file.

```text
%ProgramData%\Jaminator\cache\
├── manifest.json   (public — fetched from raw.githubusercontent.com/zachlagden/jaminator/...)
└── secrets.json    (private — fetched from api.github.com/repos/{org}/{priv-repo}/contents/...)
```

Failure modes:

- **Both online fetches succeed** → join in memory, atomic-pair-write to cache, return the joined `Manifest`. `fromCache: false`.
- **Either online fetch fails AND both cached files exist and deserialise** → load from cache, join in memory, return the joined `Manifest`. `fromCache: true`.
- **Either online fetch fails AND either cached file is missing or corrupt** → throw `InvalidOperationException` naming which file failed and why. The fetch NEVER silently returns a half-populated `Manifest`.

Documented residual (be candid):

> A crash between the two `File.Move` calls leaves one cache file fresh and the other stale. On the next successful network fetch the pair re-syncs; on the next offline launch the deserialiser detects the mismatch when the join produces unexpected `null` PSKs for SSIDs the operator expected to be populated. This is rare and benign for v0.8.0; HARDEN-06 (M3 — parallel logon-path I/O) is the planned revisit.

## Threat model — operational, not cryptographic

This section sets honest expectations for what the private secrets channel does and does not promise. The candid framing is locked by `.planning/PROJECT.md` Key Decisions row **WIFI-03** and is quoted here so the design intent is preserved on the doc surface.

> The PAT bundled in the MSI is **operational opacity**, not cryptographic protection. An attacker willing to reverse-engineer the public MSI can recover the PAT and use it to read the private secrets repo. This is **accepted by design**. The threat model:
>
> - **In scope (mitigated):** keep PSKs off the public, search-engine-indexable internet. The public `zachlagden/jaminator` repo never contains a PSK. A casual reader of the public repo cannot find PSKs by grepping or searching.
> - **Out of scope (accepted):** an attacker with the MSI can recover the PAT via static analysis (`strings`, `dotPeek`, `ILSpy`). The fine-grained PAT is read-only (`Contents: Read`) and scoped to ONLY the private secrets repo, so the blast radius is bounded — the attacker reads PSKs they could already extract from any deployed laptop via `netsh wlan show profile key=clear`. The shared exposure model is what makes the tradeoff acceptable.
> - **Mitigation:** termly rotation of both PSKs and the PAT, aligned. When a PSK rotates, the PAT rotates too — replace `installer/secrets/wifi-pat.txt`, rebuild the MSI via `installer/build.ps1`, ship a new release.
>
> Encryption-at-rest in the public manifest was considered and **rejected**: every variant ends in rot13 because the decryption key would have to be in the publicly-downloadable MSI. WIFI-03 chooses operational security (private repo + PAT) over cryptographic theatre. Full rationale: `.planning/PROJECT.md` Key Decisions row "WIFI-03".

PSK exposure on the laptop itself: any local admin can run `netsh wlan show profile name=<SSID> key=clear` and recover the PSK in plaintext. This is a Windows-level reality unrelated to Jaminator's design — same exposure as any other Wi-Fi profile distribution method including Intune. The cached `%ProgramData%\Jaminator\cache\secrets.json` file gives a non-admin local user the same data via a different read path; ACLing the cache file to admin-only is not implemented (would add NT-API surface for zero net security gain).

PSK masking in Jaminator's own logs: PSKs are masked as `***` in every log emission path in Jaminator (`%ProgramData%\Jaminator\logs\jaminator-YYYYMMDD.log` and any `%TEMP%` diagnostic logs added in Phase 4). This is enforced at the emission site, not at the DTO — see `src/Jaminator/UI/MainForm.cs` for the canonical masked-log call pattern. The literal `***` mask is the only representation of a PSK that should ever land on disk in a Jaminator-written log.
