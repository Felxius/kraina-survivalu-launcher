#requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$PublishDirectory,
    [Parameter(Mandatory)][ValidatePattern('^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(?:-[0-9A-Za-z.-]+)?$')][string]$Version,
    [Parameter(Mandatory)][string]$PrivateKeyPath,
    [Parameter(Mandatory)][string]$OutputDirectory
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$publish = [IO.Path]::GetFullPath($PublishDirectory).TrimEnd([IO.Path]::DirectorySeparatorChar)
$output = [IO.Path]::GetFullPath($OutputDirectory).TrimEnd([IO.Path]::DirectorySeparatorChar)
$keyPath = [IO.Path]::GetFullPath($PrivateKeyPath)
if ($output -eq $publish -or $output.StartsWith($publish + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Pliki wydania muszą powstać poza folderem publikacji.' }
if ($keyPath.StartsWith($publish + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -or $keyPath.StartsWith($output + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Klucz prywatny nie może znajdować się w paczce ani folderze publicznego wydania.' }
$requiredFiles = @(
    'KrainaSurvivalu.Launcher.exe', 'KrainaSurvivalu.Launcher.dll', 'KrainaSurvivalu.Launcher.Core.dll',
    'KrainaSurvivalu.Launcher.deps.json', 'KrainaSurvivalu.Launcher.runtimeconfig.json', 'config/updates.json',
    'hostfxr.dll', 'hostpolicy.dll', 'coreclr.dll', 'System.Private.CoreLib.dll',
    'PresentationCore.dll', 'PresentationFramework.dll', 'WindowsBase.dll', 'wpfgfx_cor3.dll',
    'Updater/KrainaSurvivalu.Launcher.Updater.exe', 'Updater/KrainaSurvivalu.Launcher.Updater.dll',
    'Updater/KrainaSurvivalu.Launcher.Updater.deps.json', 'Updater/KrainaSurvivalu.Launcher.Updater.runtimeconfig.json',
    'Updater/KrainaSurvivalu.Launcher.Core.dll', 'Updater/hostfxr.dll', 'Updater/hostpolicy.dll', 'Updater/coreclr.dll', 'Updater/System.Private.CoreLib.dll',
    'SteamWorker/KrainaSurvivalu.SteamWorker.exe', 'SteamWorker/KrainaSurvivalu.SteamWorker.dll',
    'SteamWorker/KrainaSurvivalu.SteamWorker.deps.json', 'SteamWorker/KrainaSurvivalu.SteamWorker.runtimeconfig.json',
    'SteamWorker/hostfxr.dll', 'SteamWorker/hostpolicy.dll', 'SteamWorker/coreclr.dll', 'SteamWorker/System.Private.CoreLib.dll'
)
foreach ($file in $requiredFiles) { if (-not (Test-Path -LiteralPath (Join-Path $publish $file) -PathType Leaf)) { throw "Niepełna paczka: $file" } }
if (Test-Path -LiteralPath (Join-Path $publish '.ks-update.lock')) { throw 'Nie pakuj folderu działającej instalacji zawierającego blokadę. Użyj czystej publikacji.' }
$binaryVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $publish 'KrainaSurvivalu.Launcher.dll')).ProductVersion
if (($binaryVersion -split '\+')[0] -cne $Version) { throw 'Wersja DLL aplikacji różni się od wersji przygotowywanego manifestu. Zbuduj publikację z tym samym -Version.' }
$items = @(Get-ChildItem -LiteralPath $publish -Recurse -Force)
if (@($items | Where-Object { ($_.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0 }).Count -ne 0) { throw 'Publikacja nie może zawierać dowiązań.' }
if (@($items | Where-Object { -not $_.PSIsContainer }).Count -gt 5000) { throw 'Zbyt wiele plików w publikacji.' }
foreach ($file in @($items | Where-Object { -not $_.PSIsContainer -and $_.Length -lt 65536 })) {
    if ($file.Extension -in @('.pem', '.key', '.pfx', '.p12') -or ([IO.File]::ReadAllText($file.FullName).Contains('-----BEGIN PRIVATE KEY-----'))) { throw "Publikacja może zawierać klucz prywatny: $($file.Name)" }
}
$configuration = Get-Content -LiteralPath (Join-Path $publish 'config/updates.json') -Raw | ConvertFrom-Json
if ([string]::IsNullOrWhiteSpace($configuration.gitHubOwner) -or [string]::IsNullOrWhiteSpace($configuration.gitHubRepository) -or [string]::IsNullOrWhiteSpace($configuration.publicKeyPem)) { throw 'Przed publikacją ustaw repozytorium i publiczny klucz aktualizacji w config/updates.json.' }
if (Test-Path -LiteralPath $output) { if (@(Get-ChildItem -LiteralPath $output -Force).Count -ne 0) { throw 'Folder wydania musi być nowy lub pusty.' } } else { [IO.Directory]::CreateDirectory($output) | Out-Null }
$key = [Security.Cryptography.ECDsa]::Create()
$publicKey = [Security.Cryptography.ECDsa]::Create()
try {
    $key.ImportFromPem([IO.File]::ReadAllText($keyPath))
    $publicKey.ImportFromPem($configuration.publicKeyPem)
    if ($key.KeySize -ne 256 -or $key.ExportParameters($false).Curve.Oid.Value -ne '1.2.840.10045.3.1.7') { throw 'Wymagany klucz ECDSA P-256.' }
    if ([Convert]::ToBase64String($key.ExportSubjectPublicKeyInfo()) -cne [Convert]::ToBase64String($publicKey.ExportSubjectPublicKeyInfo())) { throw 'Klucz prywatny nie odpowiada kluczowi publicznemu z paczki.' }
    $archive = Join-Path $output 'KS-Launcher-Windows-x64.zip'
    [IO.Compression.ZipFile]::CreateFromDirectory($publish, $archive, [IO.Compression.CompressionLevel]::Optimal, $false)
    $archiveInfo = Get-Item -LiteralPath $archive
    if ($archiveInfo.Length -gt 536870912) { throw 'Archiwum przekracza limit 512 MiB.' }
    $manifest = [ordered]@{ schemaVersion = 1; version = $Version; assetName = 'KS-Launcher-Windows-x64.zip'; archiveSize = $archiveInfo.Length; archiveSha256 = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant(); publishedAtUtc = [DateTimeOffset]::UtcNow.ToString('O') }
    $payload = [Text.UTF8Encoding]::new($false).GetBytes(($manifest | ConvertTo-Json -Compress))
    $signature = $key.SignData($payload, [Security.Cryptography.HashAlgorithmName]::SHA256, [Security.Cryptography.DSASignatureFormat]::IeeeP1363FixedFieldConcatenation)
    if (-not $publicKey.VerifyData($payload, $signature, [Security.Cryptography.HashAlgorithmName]::SHA256, [Security.Cryptography.DSASignatureFormat]::IeeeP1363FixedFieldConcatenation)) { throw 'Weryfikacja podpisu przygotowanego manifestu nie powiodła się.' }
    $envelope = [ordered]@{ payloadBase64 = [Convert]::ToBase64String($payload); signatureBase64 = [Convert]::ToBase64String($signature) }
    [IO.File]::WriteAllText((Join-Path $output 'KS-Launcher-update.json'), ($envelope | ConvertTo-Json), [Text.UTF8Encoding]::new($false))
} finally { $key.Dispose(); $publicKey.Dispose() }
Write-Host "Przygotowano ZIP i podpisany manifest wersji $Version. Opublikuj obydwa w jednym GitHub Release z tagiem v$Version."
