# Jaminator

Fleet maintenance tool for Jam Coding classroom laptops. One self-elevating EXE that pulls its config from this repo and runs cleanup, app installs, folder sync, wallpaper enforcement, and arbitrary admin commands.

## How it works

1. Tech double-clicks `Jaminator.exe` on a laptop. UAC prompts.
2. Tool fetches `manifest/manifest.json` from this repo's `main` branch.
3. UI shows what the manifest says should happen on this machine. Tech ticks what they want and clicks **Run**.
4. Each action is logged to screen and to `C:\ProgramData\Jaminator\logs\`.

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
