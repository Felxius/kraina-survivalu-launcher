#requires -Version 7.0
[CmdletBinding()]
param([Parameter(Mandatory)][string]$PrivateKeyPath, [Parameter(Mandatory)][string]$PublicKeyPath)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..')).TrimEnd([IO.Path]::DirectorySeparatorChar)
$privatePath = [IO.Path]::GetFullPath($PrivateKeyPath)
$publicPath = [IO.Path]::GetFullPath($PublicKeyPath)
if ($privatePath.StartsWith($projectRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Klucz prywatny musi być zapisany poza projektem i publicznymi paczkami.' }
if ($privatePath -eq $publicPath) { throw 'Klucz prywatny i publiczny wymagają osobnych plików.' }
foreach ($path in @($privatePath, $publicPath)) {
    if (Test-Path -LiteralPath $path) { throw "Plik już istnieje: $path" }
    if (-not (Test-Path -LiteralPath ([IO.Path]::GetDirectoryName($path)) -PathType Container)) { throw 'Najpierw utwórz prywatny folder administratora.' }
}
$key = [Security.Cryptography.ECDsa]::Create([Security.Cryptography.ECCurve+NamedCurves]::nistP256)
try {
    foreach ($entry in @(@($privatePath, $key.ExportPkcs8PrivateKeyPem()), @($publicPath, $key.ExportSubjectPublicKeyInfoPem()))) {
        $stream = [IO.File]::Open($entry[0], [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
        try { $bytes = [Text.UTF8Encoding]::new($false).GetBytes($entry[1]); $stream.Write($bytes, 0, $bytes.Length) } finally { $stream.Dispose() }
    }
} finally { $key.Dispose() }
Write-Host 'Utworzono klucze w podanych plikach. Klucz prywatny zachowaj wyłącznie u administratora; nie trafia do aplikacji.'
