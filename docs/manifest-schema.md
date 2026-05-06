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
