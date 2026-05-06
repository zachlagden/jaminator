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

Each program has per-architecture MSI URLs. The tool detects 32 vs 64-bit Windows and picks the right one.

```json
{
  "id": "scratch-desktop",
  "name": "Scratch Desktop",
  "x64": {
    "url": "https://github.com/zachlagden/jaminator/releases/download/assets-v1/scratch-desktop-x64.msi",
    "sha256": "...",
    "args": "/quiet /norestart"
  },
  "x86": {
    "url": "https://github.com/zachlagden/jaminator/releases/download/assets-v1/scratch-desktop-x86.msi",
    "sha256": "...",
    "args": "/quiet /norestart"
  },
  "detect": {
    "registryKey": "HKLM:\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\Scratch Desktop",
    "minVersion": "3.29.0"
  }
}
```

If the `detect` rule matches an already-installed equal-or-newer version, the install is skipped. Either or both arch entries may be omitted (e.g. an x64-only program).

MSIs that are too large for git are stored as GitHub Release assets, not committed to the repo.

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
