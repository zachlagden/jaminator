# Hosting installers

Jaminator's `manifest.json` references program installers by URL. Those URLs are public, so the simplest place to host them is GitHub Releases on this repo.

## Convention

The manifest expects installers at:

```
https://github.com/zachlagden/jaminator/releases/download/installers-v1/<filename>
```

Bump the tag (`installers-v1` → `installers-v2`) when you swap any installer for a new version, and update both the URLs and the `sha256` in `manifest/manifest.json` in the same commit. That keeps every laptop deterministic — never half-upgraded mid-install.

## What to upload

The first staging set is in `installers-staging/` locally (gitignored). After running the staging step you should have:

| File | Size | Purpose |
|---|---:|---|
| `KoduSetup_1.6.18.0.msi` | 149 MB | Kodu Game Lab |
| `xnafx40_redist.msi` | 7 MB | XNA prerequisite for Kodu |
| `Scratch Desktop Setup 3.9.0.exe` | 112 MB | Scratch Desktop (NSIS) |
| `makecode-arcade-setup-win64.exe` | 285 MB | MakeCode Arcade |
| `MinecraftEducationEdition_x86_1.18.32.0.exe` | 848 MB | Minecraft Education |
| `construct2-r280-setup.exe` | 1 MB | Construct 2 |
| `pivot-v5.zip` | 24 MB | Pivot Animator (portable, zip-extract) |

Total ≈ 1.4 GB.

## Upload steps

1. Tag the staging set:
   ```powershell
   git tag installers-v1
   git push origin installers-v1
   ```
2. Open https://github.com/zachlagden/jaminator/releases/new?tag=installers-v1
3. Title: "Installers v1". Drag every file from `installers-staging/` into the assets area.
4. Publish release.

After publish, every laptop running Jaminator will be able to download these on first run, sha256-verified.

## Rotating installers

To swap a program for a newer version:

1. Drop the new file into `installers-staging/`.
2. `Get-FileHash` it and grab the SHA256.
3. Edit `manifest/manifest.json`: update the `url` and `sha256` for that program.
4. Either re-upload to the same release tag (replace the asset) or cut a new tag (`installers-v2`) and update all URLs.
5. Commit and push the manifest. Done — every laptop picks up the change next launch.
