# Technology Stack

**Analysis Date:** 2026-05-11

## Languages

**Primary:**
- C# .NET 4.8 - Main application and MSI installer custom actions
- PowerShell - Configuration commands in manifest; skipIf condition evaluation
- WiX XML (WXS) - Windows Installer package definition

**Secondary:**
- Windows Batch / CMD - Legacy shell support in CommandRunner
- JSON - Manifest schema

## Runtime

**Environment:**
- .NET Framework 4.8 (WinExe desktop application)
- Windows XP SP3+ target (via TargetFramework = net48)
- Build: .NET SDK (Modern; uses Sdk="Microsoft.NET.Sdk")

**Package Manager:**
- NuGet via MSBuild/Visual Studio
- No lockfile pattern detected (standard NuGet package restore)

## Frameworks

**Core:**
- .NET Framework 4.8 - Desktop application framework
- Windows Forms (System.Windows.Forms) - UI for `MainForm.cs`

**Build/Packaging:**
- WiX Toolset v4 - MSI creation via `wix build` command
- WixToolset.Dtf.CustomAction v5.0.2 - MSI custom actions for pre/post-install hooks

**Installation:**
- Windows Installer (msiexec) - Standard MSI deployment
- Scheduled Tasks - Windows Task Scheduler integration for login-time auto-run

## Key Dependencies

**Critical:**
- Newtonsoft.Json v13.0.3 - JSON deserialization for manifest.json parsing (`Jaminator.csproj`, `Models/Manifest.cs`)
- System.Net.Http - HTTPS downloads from GitHub (manifest, wallpaper, software packages)
- System.IO.Compression - ZIP extraction for Pivot Animator and other portable installs
- Microsoft.NETFramework.ReferenceAssemblies.net48 v1.0.3 - Reference assemblies for targeting .NET 4.8

**Infrastructure:**
- WixToolset.Dtf.CustomAction v5.0.2 - Custom action injection for update checks during MSI install (`installer/UpdateCheck/UpdateCheck.csproj`)

## Configuration

**Environment:**
- Registry-based configuration (wallpaper path in `HKEY_CURRENT_USER\Control Panel\Desktop`)
- ProgramData cache: `%ProgramData%\Jaminator\cache\manifest.json` - Offline-resilient manifest fallback
- No .env files; no sensitive config detected

**Build:**
- Solution: `Jaminator.sln` (Visual Studio 2017+)
- Projects:
  - `src/Jaminator/Jaminator.csproj` - Main application (v0.1.0)
  - `bootstrap/Bootstrap.csproj` - Standalone downloader/launcher (v1.0.0)
  - `installer/UpdateCheck/UpdateCheck.csproj` - MSI custom action DLL
- MSI build: `wix build installer/installer.wxs -d Version=X.Y.Z.0 -d SourceDir=<bin/Release/net48> -o build/Jaminator.msi`

## Platform Requirements

**Development:**
- .NET SDK (any recent version with .NET 4.8 reference assemblies)
- Visual Studio 2017+ or VS Code + .NET CLI
- WiX Toolset v4+ (for `wix` CLI; not required for application-only builds)

**Production:**
- Windows 7 SP1 or later (net48 baseline)
- Administrator privileges required for:
  - MSI installation
  - Scheduled task registration (`Jaminator-Login`, `Jaminator-Daily`)
  - Registry modifications (wallpaper, GPO policies)
  - Application installs (Kodu, Scratch, Minecraft, Construct2, etc.)

**Deployment:**
- Output: Portable EXE (`Jaminator.exe`), standalone installer (`Jaminator-Setup.exe`), signed MSI (`Jaminator.msi`)
- Delivery: GitHub Releases (via GitHub API)
- No Kubernetes, no cloud runtime — pure Windows desktop

---

*Stack analysis: 2026-05-11*
