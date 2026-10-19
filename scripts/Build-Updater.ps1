[CmdletBinding()]
param([Parameter(Mandatory)][string]$DotNetPath, [Parameter(Mandatory)][string]$PublishDirectory, [string]$Version = '0.6.0-alpha', [string]$RestoreConfigFile)
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$destination = [IO.Path]::GetFullPath($PublishDirectory)
if (Test-Path -LiteralPath $destination) { if (@(Get-ChildItem -LiteralPath $destination -Force).Count -ne 0) { throw 'Folder pomocnika musi być pusty.' } }
$project = Join-Path $projectRoot 'src/KrainaSurvivalu.Launcher.Updater/KrainaSurvivalu.Launcher.Updater.csproj'
$arguments = @('publish', $project, '-c', 'Release', '-r', 'win-x64', '--self-contained', 'true', '--nologo', '-o', $destination, '-p:PublishSingleFile=false', '-p:PublishTrimmed=false', "-p:Version=$Version", "-p:InformationalVersion=$Version")
if (-not [string]::IsNullOrWhiteSpace($RestoreConfigFile)) { $arguments += "-p:RestoreConfigFile=$([IO.Path]::GetFullPath($RestoreConfigFile))" }
& $DotNetPath @arguments
if ($LASTEXITCODE -ne 0) { throw "Budowanie pomocnika nie powiodło się: $LASTEXITCODE" }
