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

Write-Host "Release-Publish fertig: $PublishDir"
