[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$IsccPath,
    [string]$DotNetPath,
    [ValidatePattern('^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$')][string]$Version = '0.7.0-alpha',
    [string]$RestoreConfigFile,
    [string]$PublishDirectory,
    [string]$OutputDirectory = 'artifacts/installer'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'Build.Common.ps1')
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$restoreConfig = Resolve-LauncherRestoreConfig -RestoreConfigFile $RestoreConfigFile -BaseDirectory $projectRoot
if (-not (Test-Path -LiteralPath $IsccPath -PathType Leaf)) {
    throw "Nie znaleziono kompilatora Inno Setup 6: $IsccPath. Zainstaluj go samodzielnie i podaj ścieżkę do ISCC.exe. Skrypt niczego nie pobiera ani nie instaluje."
}
$iscc = (Resolve-Path -LiteralPath $IsccPath).ProviderPath
if ([IO.Path]::GetFileName($iscc) -ine 'ISCC.exe') {
    throw '-IsccPath musi wskazywać plik ISCC.exe z Inno Setup 6.'
}
$isccVersion = (Get-Item -LiteralPath $iscc).VersionInfo.FileMajorPart
if ($isccVersion -ne 6) {
    throw "Wymagany jest Inno Setup 6; wersja główna wskazanego kompilatora: $isccVersion."
}
if ([string]::IsNullOrWhiteSpace($PublishDirectory)) {
    $suffix = (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 6)
    $PublishDirectory = Join-Path 'artifacts/publish/win-x64' $suffix
}
$publishPath = Get-LauncherAbsolutePath -Path $PublishDirectory -BaseDirectory $projectRoot
$installerPath = Get-LauncherAbsolutePath -Path $OutputDirectory -BaseDirectory $projectRoot
$fileVersion = ($Version -split '-', 2)[0] + '.0'

# Wszystkie argumenty są przekazywane jako osobne wartości, także ścieżki ze spacjami.
& (Join-Path $PSScriptRoot 'Build.ps1') -Configuration Release -DotNetPath $DotNetPath -Version $Version -RestoreConfigFile $restoreConfig -Publish -PublishDirectory $publishPath
if (-not (Test-Path -LiteralPath $installerPath)) {
    [IO.Directory]::CreateDirectory($installerPath) | Out-Null
}
$arguments = @(
    '/Qp', "/DAppVersion=$Version", "/DAppFileVersion=$fileVersion",
    "/DPublishDir=$publishPath", "/DInstallerOutputDir=$installerPath",
    (Join-Path $projectRoot 'installer/setup.iss')
)
& $iscc @arguments | Out-Host
if ($LASTEXITCODE -ne 0) {
    throw "Kompilowanie instalatora zakończyło się kodem $LASTEXITCODE."
}
$expectedInstaller = Join-Path $installerPath "KrainaSurvivalu-Launcher-Setup-$Version-win-x64.exe"
if (-not (Test-Path -LiteralPath $expectedInstaller -PathType Leaf)) {
    throw "Kompilator nie utworzył oczekiwanego instalatora: $expectedInstaller"
}
$digest = (Get-FileHash -LiteralPath $expectedInstaller -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText($expectedInstaller + '.sha256', $digest + '  ' + [IO.Path]::GetFileName($expectedInstaller) + [Environment]::NewLine, [Text.UTF8Encoding]::new($false))
Write-Host "Instalator: $expectedInstaller"
Write-Host "SHA-256: $digest"
Write-Host 'Instalator nie został uruchomiony. Wersja alpha jest niepodpisana.'
