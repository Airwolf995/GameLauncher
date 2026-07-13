param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$PublishDir = ".\publish\win-x64"
)

$ErrorActionPreference = "Stop"

$projectPath = ".\GameLauncher.csproj"
$nugetConfigPath = ".\NuGet.Local.config"

Write-Host "Stelle Release-Abhaengigkeiten wieder her..."
dotnet restore $projectPath -r $Runtime --configfile $nugetConfigPath

Write-Host "Erzeuge Publish-Ordner fuer den Installer..."
dotnet publish $projectPath `
    -c $Configuration `
    -r $Runtime `
    --self-contained false `
    --no-restore `
    -o $PublishDir

Copy-Item .\LICENSE (Join-Path $PublishDir "LICENSE") -Force
Copy-Item .\NOTICE (Join-Path $PublishDir "NOTICE") -Force
Copy-Item .\THIRD-PARTY-NOTICES.txt (Join-Path $PublishDir "THIRD-PARTY-NOTICES.txt") -Force
Copy-Item .\licenses (Join-Path $PublishDir "licenses") -Recurse -Force

Write-Host "Release-Publish fertig: $PublishDir"
