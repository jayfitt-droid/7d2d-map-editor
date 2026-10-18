<#
.SYNOPSIS
    Builds the self-contained editor and packs a Velopack installer (Setup.exe), portable zip and update feed.

.EXAMPLE
    ./build/release.ps1                 # version from Directory.Build.props
    ./build/release.ps1 -Version 1.0.1

    Output goes to out/releases: 7D2DMapEditor-win-Setup.exe, 7D2DMapEditor-win-Portable.zip, the .nupkg update
    packages and releases.win.json. Upload all of them to a GitHub release (or any static folder) to publish, and
    point Updates.BuiltInFeed at that place so installed copies find later versions.
#>
param(
    [string]$Version
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Push-Location $root
try {
    if (-not $Version) {
        $Version = ([xml](Get-Content 'Directory.Build.props')).Project.PropertyGroup.Version
    }
    Write-Host "Releasing 7D2D Map Editor $Version"

    dotnet test MapEditor.slnx -c Release
    if ($LASTEXITCODE) { throw 'Tests failed.' }

    $publish = Join-Path $root 'out/publish'
    if (Test-Path $publish) { Remove-Item -Recurse -Force $publish }
    dotnet publish src/MapEditor.App -c Release -r win-x64 --self-contained -p:Version=$Version -o $publish
    if ($LASTEXITCODE) { throw 'Publish failed.' }

    dotnet tool restore
    dotnet vpk pack `
        --packId 7D2DMapEditor `
        --packVersion $Version `
        --packTitle '7D2D Map Editor' `
        --packAuthors 'Jason Edwards' `
        --runtime win-x64 `
        --packDir $publish `
        --mainExe MapEditor.exe `
        --icon src/MapEditor.App/app.ico `
        --outputDir (Join-Path $root 'out/releases')
    if ($LASTEXITCODE) { throw 'Packing failed.' }

    Write-Host "Done: $(Join-Path $root 'out/releases')"
}
finally {
    Pop-Location
}
