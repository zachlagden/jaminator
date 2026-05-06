# Build the Jaminator MSI.
# Reads the tool version from Program.cs (single source of truth) and feeds it to WiX.
#
# Pre-reqs:
#   - .NET SDK 8+
#   - WiX 4 global tool: dotnet tool install --global wix
#   - The EXE must already be built (dotnet build ... -c Release)

[CmdletBinding()]
param(
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
Push-Location $repoRoot
try {
    # Discover version from Program.cs
    $programCs = Get-Content -Raw "$repoRoot\src\Jaminator\Program.cs"
    if ($programCs -notmatch 'ToolVersion\s*=\s*"([\d.]+)"') {
        throw "Could not parse ToolVersion from Program.cs"
    }
    $version = $Matches[1]
    if (($version -split '\.').Count -lt 4) { $version = "$version.0" }  # MSI wants 4 parts
    Write-Host "Building Jaminator MSI v$version"

    # Ensure EXE is built
    $binDir = "$repoRoot\src\Jaminator\bin\$Configuration\net48"
    if (-not (Test-Path "$binDir\Jaminator.exe")) {
        Write-Host "EXE not found, running dotnet build..."
        dotnet build "$repoRoot\src\Jaminator\Jaminator.csproj" -c $Configuration | Out-Host
    }

    # Build MSI
    $outDir = "$repoRoot\build"
    New-Item -ItemType Directory -Path $outDir -Force | Out-Null
    $msi = "$outDir\Jaminator.msi"
    if (Test-Path $msi) { Remove-Item $msi -Force }

    & wix build "$repoRoot\installer\installer.wxs" `
        -d "Version=$version" `
        -d "SourceDir=$binDir" `
        -bindpath "$repoRoot\installer" `
        -ext WixToolset.UI.wixext `
        -ext WixToolset.Util.wixext `
        -arch x64 `
        -o $msi
    if ($LASTEXITCODE -ne 0) { throw "wix build failed" }

    $size = [math]::Round((Get-Item $msi).Length / 1MB, 2)
    Write-Host "Built $msi ($size MB)" -ForegroundColor Green
}
finally {
    Pop-Location
}
