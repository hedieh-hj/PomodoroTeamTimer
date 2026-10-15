param(
    [string]$Runtime = "win-x64",
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$projectRoot = Split-Path -Parent $PSScriptRoot
$artifactsDir = Join-Path $projectRoot "artifacts"
$packageName = "PomodoroTeamTimer-$Runtime"
$stageDir = Join-Path $artifactsDir $packageName
$serverDir = Join-Path $stageDir "Server"
$zipPath = Join-Path $artifactsDir "$packageName.zip"

if (Test-Path -LiteralPath $stageDir) { Remove-Item -LiteralPath $stageDir -Recurse -Force }
if (Test-Path -LiteralPath $zipPath) { Remove-Item -LiteralPath $zipPath -Force }
New-Item -ItemType Directory -Path $serverDir -Force | Out-Null

dotnet publish (Join-Path $projectRoot "PomodoroTeamTimer.Server/PomodoroTeamTimer.Server.csproj") `
    --configuration $Configuration --runtime $Runtime --self-contained true `
    --output $serverDir -p:PublishSingleFile=true -p:DebugType=None -p:DebugSymbols=false
if ($LASTEXITCODE -ne 0) { throw "Server publish failed with exit code $LASTEXITCODE." }

dotnet publish (Join-Path $projectRoot "PomodoroTeamTimer.Tray/PomodoroTeamTimer.Tray.csproj") `
    --configuration $Configuration --runtime $Runtime --self-contained true `
    --output $stageDir -p:PublishSingleFile=true -p:DebugType=None -p:DebugSymbols=false
if ($LASTEXITCODE -ne 0) { throw "Tray launcher publish failed with exit code $LASTEXITCODE." }

Copy-Item -LiteralPath (Join-Path $projectRoot "README.md") -Destination $stageDir
Compress-Archive -Path (Join-Path $stageDir "*") -DestinationPath $zipPath -CompressionLevel Optimal

Write-Host "Portable package created: $zipPath"
