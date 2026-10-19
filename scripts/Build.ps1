[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Release',
    [string]$DotNetPath,
    [ValidatePattern('^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$')][string]$Version = '0.6.0-alpha',
    [string]$RestoreConfigFile,
    [switch]$Publish,
    [switch]$SkipTests,
    [string]$PublishDirectory
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'Build.Common.ps1')
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$appProject = Join-Path $projectRoot 'src/KrainaSurvivalu.Launcher.App/KrainaSurvivalu.Launcher.App.csproj'
$testProject = Join-Path $projectRoot 'tests/KrainaSurvivalu.Launcher.Core.Tests/KrainaSurvivalu.Launcher.Core.Tests.csproj'
$updaterTestProject = Join-Path $projectRoot 'tests/KrainaSurvivalu.Launcher.Updater.Tests/KrainaSurvivalu.Launcher.Updater.Tests.csproj'
$workerProject = Join-Path $projectRoot 'src/KrainaSurvivalu.SteamWorker/KrainaSurvivalu.SteamWorker.csproj'
$restoreConfig = Resolve-LauncherRestoreConfig -RestoreConfigFile $RestoreConfigFile -BaseDirectory $projectRoot
$restoreArguments = @()
if ($null -ne $restoreConfig) {
    $restoreArguments = @("-p:RestoreConfigFile=$restoreConfig")
}

Push-Location -LiteralPath $projectRoot
try {
    $dotnet = Resolve-LauncherDotNet -DotNetPath $DotNetPath
    if (-not (Test-Path -LiteralPath $appProject -PathType Leaf)) {
        throw "Brak projektu aplikacji: $appProject"
    }
    Invoke-LauncherDotNet -Executable $dotnet -Arguments (@('build', $appProject, '--configuration', $Configuration, '--nologo', "-p:Version=$Version", "-p:InformationalVersion=$Version") + $restoreArguments)
    Invoke-LauncherDotNet -Executable $dotnet -Arguments (@('build', $workerProject, '--configuration', $Configuration, '--nologo', "-p:Version=$Version", "-p:InformationalVersion=$Version") + $restoreArguments)

    if (-not $SkipTests) {
        if (-not (Test-Path -LiteralPath $testProject -PathType Leaf)) {
            throw "Brak projektu testów: $testProject"
        }
        # Core.Tests to aplikacja konsolowa; kod wyjścia 0 oznacza przejście testów.
        Invoke-LauncherDotNet -Executable $dotnet -Arguments (@('run', '--project', $testProject, '--configuration', $Configuration, '--no-launch-profile') + $restoreArguments)
        # Updater.Tests carries its own future-version fixture; do not override its Version.
        Invoke-LauncherDotNet -Executable $dotnet -Arguments (@('run', '--project', $updaterTestProject, '--configuration', $Configuration, '--no-launch-profile') + $restoreArguments)
    }

    if ($Publish) {
        if ([string]::IsNullOrWhiteSpace($PublishDirectory)) {
            $suffix = (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 6)
            $PublishDirectory = Join-Path 'artifacts/publish/win-x64' $suffix
        }
        $publishPath = Get-LauncherAbsolutePath -Path $PublishDirectory -BaseDirectory $projectRoot
        if (Test-Path -LiteralPath $publishPath) {
            if (-not (Test-Path -LiteralPath $publishPath -PathType Container)) {
                throw "Ścieżka publikacji nie jest folderem: $publishPath"
            }
            if (@(Get-ChildItem -LiteralPath $publishPath -Force).Count -gt 0) {
                throw "Folder publikacji musi być pusty, aby uniknąć dołączenia starych plików. Wybierz nowy -PublishDirectory: $publishPath"
            }
        }
        Invoke-LauncherDotNet -Executable $dotnet -Arguments (@(
            'publish', $appProject, '--configuration', $Configuration, '--runtime', 'win-x64',
            '--self-contained', 'true', '--output', $publishPath, '--nologo',
            '-p:PublishTrimmed=false', '-p:PublishSingleFile=false', '-p:PublishReadyToRun=false', "-p:Version=$Version", "-p:InformationalVersion=$Version"
        ) + $restoreArguments)
        foreach ($helper in @(
            @{ Project = $workerProject; Directory = 'SteamWorker' },
            @{ Project = (Join-Path $projectRoot 'src/KrainaSurvivalu.Launcher.Updater/KrainaSurvivalu.Launcher.Updater.csproj'); Directory = 'Updater' }
        )) {
            Invoke-LauncherDotNet -Executable $dotnet -Arguments (@(
                'publish', $helper.Project, '--configuration', $Configuration, '--runtime', 'win-x64',
                '--self-contained', 'true', '--output', (Join-Path $publishPath $helper.Directory), '--nologo',
                '-p:PublishTrimmed=false', '-p:PublishSingleFile=false', '-p:PublishReadyToRun=false', "-p:Version=$Version", "-p:InformationalVersion=$Version"
            ) + $restoreArguments)
        }
        foreach ($relativeFile in @('KrainaSurvivalu.Launcher.exe', 'config/servers.json', 'config/community.json', 'config/updates.json', 'SteamWorker/KrainaSurvivalu.SteamWorker.exe', 'Updater/KrainaSurvivalu.Launcher.Updater.exe')) {
            if (-not (Test-Path -LiteralPath (Join-Path $publishPath $relativeFile) -PathType Leaf)) {
                throw "Publikacja nie zawiera wymaganego pliku: $relativeFile"
            }
        }
        foreach ($configName in @('servers.json', 'community.json', 'updates.json')) {
            $sourceConfig = Get-FileHash -LiteralPath (Join-Path $projectRoot ('config/' + $configName)) -Algorithm SHA256
            $publishedConfig = Get-FileHash -LiteralPath (Join-Path $publishPath ('config/' + $configName)) -Algorithm SHA256
            if ($sourceConfig.Hash -ne $publishedConfig.Hash) {
                throw "Opublikowana konfiguracja różni się od źródła: config/$configName."
            }
        }
        Write-Host "Gotowa aplikacja Windows x64: $publishPath"
    }
    Write-Host 'Budowanie zakończone.'
}
finally {
    Pop-Location
}
