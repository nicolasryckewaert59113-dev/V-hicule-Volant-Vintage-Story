param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"

$workspacePath = [IO.Path]::GetFullPath($PSScriptRoot)
$stagingPath = [IO.Path]::GetFullPath((Join-Path $workspacePath "dist\IndependentVehicles-0.5.0"))
$archivePath = [IO.Path]::GetFullPath((Join-Path $workspacePath "dist\IndependentVehicles-0.5.0.zip"))

if (-not $stagingPath.StartsWith($workspacePath, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Le dossier de préparation sort de l’espace de travail."
}

dotnet build (Join-Path $workspacePath "src\IndependentVehicles.csproj") -c $Configuration
if ($LASTEXITCODE -ne 0) { throw "La compilation a échoué." }

if (Test-Path -LiteralPath $stagingPath) {
    Remove-Item -LiteralPath $stagingPath -Recurse -Force
}
New-Item -ItemType Directory -Path $stagingPath | Out-Null

Copy-Item -LiteralPath (Join-Path $workspacePath "modinfo.json") -Destination $stagingPath
Copy-Item -LiteralPath (Join-Path $workspacePath "assets") -Destination $stagingPath -Recurse
Copy-Item -LiteralPath (Join-Path $workspacePath "src\bin\$Configuration\net10.0\IndependentVehicles.dll") -Destination $stagingPath

if (Test-Path -LiteralPath $archivePath) {
    Remove-Item -LiteralPath $archivePath -Force
}
Compress-Archive -Path (Join-Path $stagingPath "*") -DestinationPath $archivePath

Write-Host "Paquet créé : $archivePath"
