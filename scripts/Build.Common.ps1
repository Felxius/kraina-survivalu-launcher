Set-StrictMode -Version Latest

function Resolve-LauncherDotNet {
    param([string]$DotNetPath)

    if (-not [string]::IsNullOrWhiteSpace($DotNetPath)) {
        $candidate = $DotNetPath
    }
    elseif (-not [string]::IsNullOrWhiteSpace($env:DOTNET_ROOT)) {
        $candidate = Join-Path $env:DOTNET_ROOT 'dotnet.exe'
    }
    else {
        $command = Get-Command dotnet.exe -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($null -eq $command) {
            throw 'Nie znaleziono .NET 10 SDK. Zainstaluj SDK albo podaj -DotNetPath do pliku dotnet.exe.'
        }
        $candidate = $command.Source
    }

    if (-not (Test-Path -LiteralPath $candidate -PathType Leaf)) {
        throw "Nie istnieje plik dotnet.exe: $candidate"
    }
    $resolved = (Resolve-Path -LiteralPath $candidate).ProviderPath
    $versionLines = @(& $resolved --version 2>&1)
    if ($LASTEXITCODE -ne 0) {
        throw "Nie można uruchomić .NET SDK: $($versionLines -join [Environment]::NewLine)"
    }
    $version = ($versionLines | Select-Object -Last 1).ToString().Trim()
    if ($version -notmatch '^10\.\d+\.\d+(?:[-+].+)?$') {
        throw "Projekt wymaga .NET 10 SDK. Aktywny SDK: $version. Sprawdź dotnet --list-sdks oraz global.json."
    }
    Write-Host "SDK: $version"
    return $resolved
}

function Invoke-LauncherDotNet {
    param(
        [Parameter(Mandatory = $true)][string]$Executable,
        [Parameter(Mandatory = $true)][string[]]$Arguments
    )
    # Keep native output on the pipeline so callers can capture the full build log.
    & $Executable @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Polecenie dotnet $($Arguments[0]) zakończyło się kodem $LASTEXITCODE."
    }
}

function Get-LauncherAbsolutePath {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$BaseDirectory
    )
    if ([IO.Path]::IsPathRooted($Path)) {
        return [IO.Path]::GetFullPath($Path)
    }
    return [IO.Path]::GetFullPath((Join-Path $BaseDirectory $Path))
}

function Resolve-LauncherRestoreConfig {
    param(
        [string]$RestoreConfigFile,
        [Parameter(Mandatory = $true)][string]$BaseDirectory
    )
    if ([string]::IsNullOrWhiteSpace($RestoreConfigFile)) {
        return $null
    }
    $resolved = Get-LauncherAbsolutePath -Path $RestoreConfigFile -BaseDirectory $BaseDirectory
    if (-not (Test-Path -LiteralPath $resolved -PathType Leaf)) {
        throw "Nie istnieje plik konfiguracji NuGet: $resolved"
    }
    return (Resolve-Path -LiteralPath $resolved).ProviderPath
}
