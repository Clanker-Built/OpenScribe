#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Creates a self-signed code signing certificate for OpenScribe.

.DESCRIPTION
    One-time script that creates a self-signed code signing certificate,
    exports the .pfx (private key) and .cer (public key) files.

    The .pfx stays on the build machine (never committed to source control).
    The .cer is distributed to client machines via GPO for trust.

.PARAMETER Subject
    Certificate subject name. Default: "OpenScribe Code Signing"

.PARAMETER ValidYears
    Number of years the certificate is valid. Default: 3

.PARAMETER OutputDir
    Directory to export certificate files. Default: build\certs

.EXAMPLE
    .\Create-CodeSigningCert.ps1
    .\Create-CodeSigningCert.ps1 -Subject "My Org Code Signing" -ValidYears 5
#>

[CmdletBinding()]
param(
    [string]$Subject = "OpenScribe Code Signing",
    [int]$ValidYears = 3,
    [string]$OutputDir
)

$ErrorActionPreference = 'Stop'

if (-not $OutputDir) {
    $OutputDir = Join-Path $PSScriptRoot "certs"
}

if (-not (Test-Path $OutputDir)) {
    New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
}

$pfxPath = Join-Path $OutputDir "OpenScribe-CodeSigning.pfx"
$cerPath = Join-Path $OutputDir "OpenScribe-CodeSigning.cer"

# Check if cert files already exist
if ((Test-Path $pfxPath) -or (Test-Path $cerPath)) {
    Write-Warning "Certificate files already exist in $OutputDir"
    $response = Read-Host "Overwrite? (y/N)"
    if ($response -ne 'y') {
        Write-Host "Aborted." -ForegroundColor Yellow
        return
    }
}

Write-Host "Creating self-signed code signing certificate..." -ForegroundColor Cyan
Write-Host "  Subject: CN=$Subject" -ForegroundColor Gray
Write-Host "  Valid:   $ValidYears years" -ForegroundColor Gray

$notAfter = (Get-Date).AddYears($ValidYears)

$cert = New-SelfSignedCertificate `
    -Type CodeSigningCert `
    -Subject "CN=$Subject" `
    -FriendlyName $Subject `
    -CertStoreLocation Cert:\CurrentUser\My `
    -NotAfter $notAfter `
    -KeyAlgorithm RSA `
    -KeyLength 2048 `
    -HashAlgorithm SHA256

Write-Host "Certificate created. Thumbprint: $($cert.Thumbprint)" -ForegroundColor Green

# Prompt for PFX password
$pfxPassword = Read-Host -Prompt "Enter password for .pfx file" -AsSecureString

# Export PFX (private key)
Export-PfxCertificate -Cert $cert -FilePath $pfxPath -Password $pfxPassword | Out-Null
Write-Host "Exported PFX (private key): $pfxPath" -ForegroundColor Green

# Export CER (public key only)
Export-Certificate -Cert $cert -FilePath $cerPath | Out-Null
Write-Host "Exported CER (public key):  $cerPath" -ForegroundColor Green

Write-Host ""
Write-Host "=== Next Steps ===" -ForegroundColor Cyan
Write-Host "1. Keep the .pfx file secure on the build machine. NEVER commit it to source control." -ForegroundColor Yellow
Write-Host "2. Distribute the .cer file to client machines via GPO:" -ForegroundColor White
Write-Host "   Computer Configuration > Windows Settings > Security Settings >" -ForegroundColor Gray
Write-Host "   Public Key Policies > Trusted Publishers" -ForegroundColor Gray
Write-Host ""
Write-Host "3. Use the thumbprint in your build command:" -ForegroundColor White
Write-Host "   .\build\Build-OpenScribe.ps1 -CertThumbprint `"$($cert.Thumbprint)`"" -ForegroundColor Gray
Write-Host ""
Write-Host "Certificate Thumbprint: $($cert.Thumbprint)" -ForegroundColor Cyan
