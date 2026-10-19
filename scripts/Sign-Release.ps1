[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [Parameter(Mandatory = $true)][string[]]$FilePath,
    [Parameter(Mandatory = $true)][string]$SignToolPath,
    [Parameter(Mandatory = $true)][ValidatePattern('^[0-9A-Fa-f]{40}$')][string]$CertificateThumbprint,
    [ValidateSet('CurrentUser', 'LocalMachine')][string]$CertificateStoreLocation = 'CurrentUser',
    [Parameter(Mandatory = $true)][string]$TimestampUrl,
    [string]$ReportPath
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$timestampUri = $null
if ($TimestampUrl -ne $TimestampUrl.Trim() -or $TimestampUrl.Contains('\') -or
    $TimestampUrl.ToCharArray().Where({ [char]::IsControl($_) }).Count -gt 0 -or
    -not [Uri]::TryCreate($TimestampUrl, [UriKind]::Absolute, [ref]$timestampUri) -or
    $timestampUri.Scheme -ne 'https' -or [string]::IsNullOrWhiteSpace($timestampUri.Host) -or
    $timestampUri.UserInfo.Length -gt 0 -or $timestampUri.Fragment.Length -gt 0) {
    throw 'Podaj rzeczywisty adres HTTPS serwera RFC 3161 dostawcy certyfikatu, bez danych logowania i fragmentu.'
}
if (-not (Test-Path -LiteralPath $SignToolPath -PathType Leaf)) {
    throw "Nie znaleziono SignTool: $SignToolPath. Podaj istniejący signtool.exe z Windows SDK."
}
$signTool = (Resolve-Path -LiteralPath $SignToolPath).ProviderPath
if ([IO.Path]::GetFileName($signTool) -ine 'signtool.exe') {
    throw '-SignToolPath musi wskazywać signtool.exe z Windows SDK.'
}

$thumbprint = $CertificateThumbprint.ToUpperInvariant()
$certificatePath = "Cert:\$CertificateStoreLocation\My\$thumbprint"
$certificate = Get-Item -LiteralPath $certificatePath
if (-not $certificate.HasPrivateKey) { throw 'Wybrany certyfikat nie ma dostępnego klucza prywatnego.' }
if ($certificate.Subject -eq $certificate.Issuer) { throw 'Ten skrypt wydawniczy nie używa certyfikatów samopodpisanych.' }
$now = [DateTime]::UtcNow
if ($now -lt $certificate.NotBefore.ToUniversalTime() -or $now -gt $certificate.NotAfter.ToUniversalTime()) {
    throw 'Certyfikat nie jest obecnie ważny.'
}
$usageExtension = $certificate.Extensions | Where-Object { $_.Oid.Value -eq '2.5.29.37' } | Select-Object -First 1
if ($null -eq $usageExtension) { throw 'Certyfikat musi mieć jawne rozszerzenie EKU Code Signing.' }
$decodedUsage = [System.Security.Cryptography.X509Certificates.X509EnhancedKeyUsageExtension]::new($usageExtension, $usageExtension.Critical)
$usageOids = @($decodedUsage.EnhancedKeyUsages | ForEach-Object { $_.Value })
if ($usageOids -notcontains '1.3.6.1.5.5.7.3.3') { throw 'Certyfikat musi mieć przeznaczenie Code Signing.' }

$files = @()
$seen = @{}
foreach ($candidate in $FilePath) {
    if (-not (Test-Path -LiteralPath $candidate -PathType Leaf)) { throw "Nie istnieje plik do podpisania: $candidate" }
    $path = (Resolve-Path -LiteralPath $candidate).ProviderPath
    if ($seen.ContainsKey($path)) { throw "Powtórzony plik: $path" }
    $seen[$path] = $true
    $name = [IO.Path]::GetFileName($path)
    if ($name -notmatch '^(KrainaSurvivalu\.Launcher(?:\.Core)?\.(?:exe|dll)|KrainaSurvivalu-Launcher-Setup-[0-9A-Za-z.-]+-win-x64\.exe)$') {
        throw "Podpisuj wyłącznie własne pliki aplikacji lub instalatora: $name"
    }
    $existing = Get-AuthenticodeSignature -LiteralPath $path
    if ($null -ne $existing.SignerCertificate -or $existing.Status -ne 'NotSigned') {
        throw "Plik nie jest świeżym niepodpisanym plikiem PE. Nie zastępuję istniejącego podpisu: $path"
    }
    $files += [pscustomobject]@{ Path = $path; BeforeSha256 = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant() }
}
if ($files.Count -eq 0) { throw 'Wskaż co najmniej jeden własny plik aplikacji lub instalatora.' }
if ([string]::IsNullOrWhiteSpace($ReportPath)) {
    $suffix = (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 6)
    $ReportPath = Join-Path $PSScriptRoot "../artifacts/signing/$suffix.json"
}
$reportFile = [IO.Path]::GetFullPath($ReportPath)
if (Test-Path -LiteralPath $reportFile) { throw "Raport już istnieje; wybierz nową ścieżkę: $reportFile" }

$results = @()
$failure = $null
$attemptedSigning = $false
try {
    foreach ($file in $files) {
        if (-not $PSCmdlet.ShouldProcess($file.Path, 'Podpisz SHA-256 istniejącym certyfikatem i oznacz czasem RFC 3161')) { continue }
        $attemptedSigning = $true
        $arguments = @('sign', '/s', 'My', '/sha1', $thumbprint, '/u', '1.3.6.1.5.5.7.3.3',
            '/fd', 'SHA256', '/tr', $TimestampUrl, '/td', 'SHA256', '/d', 'Kraina Survivalu Launcher')
        if ($CertificateStoreLocation -eq 'LocalMachine') { $arguments += '/sm' }
        $arguments += $file.Path
        $signOutput = @(& $signTool @arguments 2>&1)
        $signExitCode = $LASTEXITCODE
        $signOutput | ForEach-Object { Write-Host $_ }
        if ($signExitCode -ne 0) { throw "Podpisywanie zakończyło się kodem $signExitCode; plik wymaga sprawdzenia: $($file.Path)" }

        $verifyOutput = @(& $signTool verify /pa /all /tw /v $file.Path 2>&1)
        $verifyExitCode = $LASTEXITCODE
        $verifyOutput | ForEach-Object { Write-Host $_ }
        if ($verifyExitCode -ne 0) { throw "Weryfikacja podpisu lub timestampu zakończyła się kodem ${verifyExitCode}: $($file.Path)" }
        $signature = Get-AuthenticodeSignature -LiteralPath $file.Path
        if ($signature.Status -ne 'Valid' -or $null -eq $signature.SignerCertificate -or
            $signature.SignerCertificate.Thumbprint -ne $thumbprint -or $null -eq $signature.TimeStamperCertificate) {
            throw "Nie potwierdzono ważnego podpisu wybranego wydawcy i znacznika czasu: $($file.Path)"
        }
        $results += [ordered]@{
            path = $file.Path; status = 'PASS'; beforeSha256 = $file.BeforeSha256
            signedSha256 = (Get-FileHash -LiteralPath $file.Path -Algorithm SHA256).Hash.ToLowerInvariant()
            signerThumbprint = $signature.SignerCertificate.Thumbprint
            timestampSignerSubject = $signature.TimeStamperCertificate.Subject
            signExitCode = $signExitCode; verifyExitCode = $verifyExitCode
            signOutput = @($signOutput | ForEach-Object { $_.ToString() })
            verifyOutput = @($verifyOutput | ForEach-Object { $_.ToString() })
        }
    }
}
catch { $failure = $_.Exception.Message }
finally {
    if ($attemptedSigning) {
        [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($reportFile)) | Out-Null
        $report = [ordered]@{
            schemaVersion = 1; status = $(if ($null -eq $failure -and $results.Count -eq $files.Count) { 'PASS' } else { 'FAIL' })
            certificateThumbprint = $thumbprint; certificateSubject = $certificate.Subject
            certificateStore = "$CertificateStoreLocation/My"; timestampUrl = $TimestampUrl
            digestAlgorithm = 'SHA256'; timestampProtocol = 'RFC3161'; files = $results
            requestedPaths = @($files | ForEach-Object { $_.Path }); error = $failure
            antivirusVerdict = 'NOT_TESTED'; smartScreenReputation = 'NOT_TESTED'
        }
        [IO.File]::WriteAllText($reportFile, ($report | ConvertTo-Json -Depth 6), [Text.UTF8Encoding]::new($false))
        Write-Host "Raport podpisywania: $reportFile"
    }
}
if ($null -ne $failure) { throw $failure }
if ($attemptedSigning) { Write-Host 'Podpisy i znaczniki czasu zweryfikowane. Wykonaj ponownie testy i odciski podpisanych plików przed pakowaniem.' }
