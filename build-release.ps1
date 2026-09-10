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

# "dotnet publish" schreibt nur hinzu und raeumt nie auf. Ohne dieses Leeren
# waere der Ordner die Summe aller bisherigen Durchlaeufe: Dateien entfallener
# Abhaengigkeiten - Bibliotheken wie Lizenztexte - blieben liegen und landeten
# im Installer.
Write-Host "Leere Publish-Ordner..."
Remove-Item $PublishDir -Recurse -Force -ErrorAction SilentlyContinue

Write-Host "Erzeuge Publish-Ordner fuer den Installer..."
dotnet publish $projectPath `
    -c $Configuration `
    -r $Runtime `
    --self-contained false `
    --no-restore `
    -o $PublishDir

Copy-Item .\LICENSE (Join-Path $PublishDir "LICENSE") -Force
Copy-Item .\COPYRIGHT.txt (Join-Path $PublishDir "COPYRIGHT.txt") -Force
Remove-Item (Join-Path $PublishDir "COPYRIGHT-NOTICE.txt") -Force -ErrorAction SilentlyContinue
Remove-Item (Join-Path $PublishDir "NOTICE") -Force -ErrorAction SilentlyContinue
Copy-Item .\THIRD-PARTY-NOTICES.txt (Join-Path $PublishDir "THIRD-PARTY-NOTICES.txt") -Force
Copy-Item .\PRIVACY.md (Join-Path $PublishDir "PRIVACY.md") -Force
$licensesOutputDir = Join-Path $PublishDir "licenses"
New-Item -ItemType Directory -Path $licensesOutputDir -Force | Out-Null
Copy-Item .\licenses\* $licensesOutputDir -Recurse -Force

Write-Host "Release-Publish fertig: $PublishDir"
