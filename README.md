# Jaminator

Fleet maintenance tool for Jam Coding classroom laptops. One self-elevating EXE that pulls its config from this repo and runs cleanup, app installs, folder sync, wallpaper enforcement, and arbitrary admin commands.

## How it works

1. Tech runs `Jaminator.exe --install` once per laptop. UAC prompts. Jaminator copies itself to `C:\Program Files\Jaminator\` and registers a scheduled task that auto-runs at every user logon.
2. **At logon (auto, headless)** Jaminator runs only the *login-safe* sections:
   - `folders` — ensure school folders exist under `Documents/`
   - `wallpaper` — refresh canonical wallpaper, revert any kid-change
3. **On demand (UI, by tech)** the tech opens Jaminator from the Start Menu to run anything *disruptive* — cleanup, app installs, registry tweaks. None of these run at logon, so a class in progress is never interrupted.
4. The whole control plane lives in `manifest/manifest.json` in this repo. Edit + commit and every laptop picks up the change next launch.
5. Every action is logged to screen and to `C:\ProgramData\Jaminator\logs\`.

## CLI modes

| Flag | Behaviour |
|---|---|
| *(none)* | Open the full UI. Tech ticks sections + clicks Run. |
| `--login-mode` | Headless. Run only login-safe sections (folders + wallpaper). Used by the scheduled task. |
| `--run-all` | Headless. Run every section non-interactively. Useful for scripted/Tailscale-driven setup. |
| `--install` | Self-install to `%ProgramFiles%\Jaminator\`, register scheduled task, create Start Menu shortcut, exit. |
| `--uninstall` | Reverse of `--install`. |

## Repo layout

```
manifest/manifest.json    # live config (folders, apps, commands, wallpaper)
assets/                    # wallpaper.png and other static assets
src/Jaminator/             # C#/.NET Framework 4.8 WinForms project
docs/                      # schema docs
```

## Updating the fleet

You don't redeploy the EXE for config changes — just edit `manifest/manifest.json` and commit. Every laptop picks up the change next time it runs.

For a new version of the tool itself: tag a release in GitHub with the EXE attached. Jaminator self-updates on next launch if a newer tag exists.

## Manifest schema

See [docs/manifest-schema.md](docs/manifest-schema.md).

## Building

Requires .NET SDK 8+ (only for the build — produces a .NET Framework 4.8 EXE that runs on any Win10/11, 32 or 64-bit, with no runtime install).

```powershell
dotnet build src/Jaminator -c Release
```

Output: `src/Jaminator/bin/Release/net48/Jaminator.exe`

## Security model

- Public repo. Anyone can read; only repo collaborators can push.
- Every download (MSI, wallpaper) is verified against a sha256 in the manifest. Tampered downloads are refused.
- Tool executes arbitrary PowerShell from the manifest, so the manifest itself is the trust root. Treat write access to this repo as admin access to every Jam Coding laptop.
