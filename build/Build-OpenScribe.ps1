<#
.SYNOPSIS
    Builds, signs, and packages the OpenScribe application.

.DESCRIPTION
    Main build script that publishes the OpenScribe app as a self-contained deployment,
    optionally signs the binaries, and creates an Inno Setup installer.

.PARAMETER Architecture
    Target architecture: x64 or arm64. Default: x64

.PARAMETER Version
    Version string (e.g., "1.0.0"). Default: "1.0.0"

.PARAMETER CertThumbprint
    Thumbprint of the code signing certificate in the certificate store.
    Mutually exclusive with CertPfxPath.

.PARAMETER CertPfxPath
    Path to a .pfx file for code signing. Will prompt for password.
    Mutually exclusive with CertThumbprint.

.PARAMETER SkipSign
    Skip code signing of binaries and installer.

.PARAMETER SkipInstaller
    Skip Inno Setup installer creation. Only publish and optionally sign.

.PARAMETER Configuration
    Build configuration. Default: Release

.EXAMPLE
    .\Build-OpenScribe.ps1 -Architecture x64 -Version "1.0.0" -CertThumbprint "ABC123..."
    .\Build-OpenScribe.ps1 -SkipSign -SkipInstaller
    .\Build-OpenScribe.ps1 -Architecture arm64 -Version "2.0.0" -CertPfxPath ".\build\certs\OpenScribe-CodeSigning.pfx"
#>

[CmdletBinding()]
param(
    [ValidateSet("x64", "arm64")]
    [string]$Architecture = "x64",

    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version = "1.0.0",

    [string]$CertThumbprint,

    [string]$CertPfxPath,

    [switch]$SkipSign,

    [switch]$SkipInstaller,

    [ValidateSet("Release", "Debug")]
    [string]$Configuration = "Release"
)

$ErrorActionPreference = 'Stop'

# --- Paths ---
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$buildDir = $PSScriptRoot
$appProject = Join-Path $repoRoot "src\OpenScribe.App\OpenScribe.App.csproj"
$rid = "win-$Architecture"
$publishDir = Join-Path $buildDir "publish\$rid"
$artifactsDir = Join-Path $buildDir "artifacts"
$issFile = Join-Path $buildDir "OpenScribe.iss"
$installerName = "OpenScribe-$Version-$Architecture-Setup"

# --- Banner ---
Write-Host ""
Write-Host "============================================" -ForegroundColor Cyan
Write-Host "  OpenScribe Build Script" -ForegroundColor Cyan
Write-Host "============================================" -ForegroundColor Cyan
Write-Host "  Version:       $Version" -ForegroundColor White
Write-Host "  Architecture:  $Architecture ($rid)" -ForegroundColor White
Write-Host "  Configuration: $Configuration" -ForegroundColor White
Write-Host "  Sign:          $(-not $SkipSign)" -ForegroundColor White
Write-Host "  Installer:     $(-not $SkipInstaller)" -ForegroundColor White
Write-Host "============================================" -ForegroundColor Cyan
Write-Host ""

# --- Validate parameters ---
if (-not $SkipSign) {
    if ($CertThumbprint -and $CertPfxPath) {
        Write-Error "Specify either -CertThumbprint or -CertPfxPath, not both."
        return
    }
    if (-not $CertThumbprint -and -not $CertPfxPath) {
        Write-Error "Code signing requires -CertThumbprint or -CertPfxPath. Use -SkipSign to skip signing."
        return
    }
    if ($CertPfxPath -and -not (Test-Path $CertPfxPath)) {
        Write-Error "PFX file not found: $CertPfxPath"
        return
    }
}

# --- Step 1: Validate prerequisites ---
Write-Host "[1/5] Validating prerequisites..." -ForegroundColor Cyan

# Check dotnet SDK
$dotnetVersion = & dotnet --version 2>$null
if (-not $dotnetVersion) {
    Write-Error "dotnet SDK not found. Install .NET 8 SDK from https://dot.net"
    return
}
Write-Host "  dotnet SDK: $dotnetVersion" -ForegroundColor Gray

# Check signtool (only if signing)
if (-not $SkipSign) {
    $signtool = Get-Command signtool.exe -ErrorAction SilentlyContinue
    if (-not $signtool) {
        # Try common Windows SDK paths
        $sdkPaths = @(
            "${env:ProgramFiles(x86)}\Windows Kits\10\bin\*\x64\signtool.exe"
            "${env:ProgramFiles(x86)}\Windows Kits\10\App Certification Kit\signtool.exe"
        )
        $signtoolPath = $sdkPaths | ForEach-Object { Resolve-Path $_ -ErrorAction SilentlyContinue } |
            Sort-Object -Descending | Select-Object -First 1

        if ($signtoolPath) {
            $signtoolExe = $signtoolPath.Path
            Write-Host "  signtool:   $signtoolExe" -ForegroundColor Gray
        } else {
            Write-Error "signtool.exe not found. Install Windows SDK or add signtool to PATH."
            return
        }
    } else {
        $signtoolExe = $signtool.Source
        Write-Host "  signtool:   $signtoolExe" -ForegroundColor Gray
    }
}

# Check Inno Setup (only if building installer)
if (-not $SkipInstaller) {
    $iscc = Get-Command iscc.exe -ErrorAction SilentlyContinue
    if (-not $iscc) {
        # Try common Inno Setup paths
        $isccPaths = @(
            "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe"
            "${env:ProgramFiles}\Inno Setup 6\ISCC.exe"
        )
        $isccPath = $isccPaths | Where-Object { Test-Path $_ } | Select-Object -First 1

        if ($isccPath) {
            $isccExe = $isccPath
            Write-Host "  Inno Setup: $isccExe" -ForegroundColor Gray
        } else {
            Write-Error "Inno Setup 6 (ISCC.exe) not found. Install from https://jrsoftware.org/isinfo.php or add to PATH."
            return
        }
    } else {
        $isccExe = $iscc.Source
        Write-Host "  Inno Setup: $isccExe" -ForegroundColor Gray
    }
}

Write-Host "  Prerequisites OK" -ForegroundColor Green
Write-Host ""

# --- Step 2: Publish ---
Write-Host "[2/5] Publishing OpenScribe ($rid, self-contained)..." -ForegroundColor Cyan

# Clean previous publish output
if (Test-Path $publishDir) {
    Remove-Item -Recurse -Force $publishDir
}

# Use PublishDir property instead of --output to preserve WinUI 3 resource pipeline.
# The --output flag bypasses MSBuild targets that copy .pri and .xbf files.
$publishArgs = @(
    "publish"
    $appProject
    "--configuration", $Configuration
    "--runtime", $rid
    "--self-contained", "true"
    "-p:Platform=$Architecture"
    "-p:PublishDir=$publishDir\"
    "-p:WindowsAppSDKSelfContained=true"
    "-p:PublishReadyToRun=true"
    "-p:DebugType=none"
    "-p:DebugSymbols=false"
    "-p:Version=$Version"
    "-p:AssemblyVersion=$Version.0"
    "-p:FileVersion=$Version.0"
)

Write-Host "  dotnet $($publishArgs -join ' ')" -ForegroundColor Gray
& dotnet @publishArgs

if ($LASTEXITCODE -ne 0) {
    Write-Error "dotnet publish failed with exit code $LASTEXITCODE"
    return
}

# Verify critical WinUI 3 resources made it to the publish folder
$priFile = Join-Path $publishDir "OpenScribe.App.pri"
if (-not (Test-Path $priFile)) {
    Write-Warning "OpenScribe.App.pri missing from publish output — copying from build output..."
    $appDir = Split-Path $appProject
    $buildPri = Join-Path $appDir "bin\$Architecture\$Configuration\net8.0-windows10.0.22621.0\$rid\OpenScribe.App.pri"
    if (Test-Path $buildPri) {
        Copy-Item $buildPri $publishDir -Force
        Write-Host "  Copied OpenScribe.App.pri" -ForegroundColor Gray
    } else {
        Write-Error "Cannot find OpenScribe.App.pri in build output ($buildPri). XAML resources will fail at runtime."
        return
    }
}

# Copy .xbf compiled XAML files if missing
$publishedXbf = Get-ChildItem -Path $publishDir -Filter "*.xbf" -Recurse -ErrorAction SilentlyContinue
if ($publishedXbf.Count -eq 0) {
    Write-Warning "Compiled XAML (.xbf) files missing from publish output — copying from build output..."
    $appDir = Split-Path $appProject
    $buildDir_xbf = Join-Path $appDir "bin\$Architecture\$Configuration\net8.0-windows10.0.22621.0\$rid"
    $xbfFiles = Get-ChildItem -Path $buildDir_xbf -Filter "*.xbf" -Recurse -ErrorAction SilentlyContinue
    foreach ($xbf in $xbfFiles) {
        $relativePath = $xbf.FullName.Substring($buildDir_xbf.Length)
        $destPath = Join-Path $publishDir $relativePath
        $destDir = Split-Path $destPath
        if (-not (Test-Path $destDir)) {
            New-Item -ItemType Directory -Path $destDir -Force | Out-Null
        }
        Copy-Item $xbf.FullName $destPath -Force
        Write-Host "  Copied $relativePath" -ForegroundColor Gray
    }
}

Write-Host "  Published to: $publishDir" -ForegroundColor Green
Write-Host ""

# --- Step 3: Sign binaries ---
if (-not $SkipSign) {
    Write-Host "[3/5] Signing OpenScribe binaries..." -ForegroundColor Cyan

    # Sign only OpenScribe.* files (not third-party DLLs)
    $filesToSign = Get-ChildItem -Path $publishDir -Include "OpenScribe.*.dll", "OpenScribe.*.exe" -Recurse

    if ($filesToSign.Count -eq 0) {
        Write-Warning "No OpenScribe.* files found to sign in $publishDir"
    } else {
        Write-Host "  Found $($filesToSign.Count) file(s) to sign" -ForegroundColor Gray

        $signArgs = @("sign", "/fd", "SHA256", "/td", "SHA256")

        if ($CertThumbprint) {
            $signArgs += @("/sha1", $CertThumbprint)
        } elseif ($CertPfxPath) {
            $pfxPassword = Read-Host -Prompt "Enter PFX password" -AsSecureString
            $bstr = [System.Runtime.InteropServices.Marshal]::SecureStringToBSTR($pfxPassword)
            $plainPassword = [System.Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr)
            [System.Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr)
            $signArgs += @("/f", $CertPfxPath, "/p", $plainPassword)
        }

        foreach ($file in $filesToSign) {
            $fileSignArgs = $signArgs + @($file.FullName)
            Write-Host "  Signing: $($file.Name)" -ForegroundColor Gray
            & $signtoolExe @fileSignArgs 2>&1 | Out-Null
            if ($LASTEXITCODE -ne 0) {
                Write-Error "Failed to sign $($file.Name)"
                return
            }
        }

        Write-Host "  All binaries signed" -ForegroundColor Green
    }
} else {
    Write-Host "[3/5] Skipping code signing (--SkipSign)" -ForegroundColor Yellow
}
Write-Host ""

# --- Step 4: Create installer ---
if (-not $SkipInstaller) {
    Write-Host "[4/5] Creating Inno Setup installer..." -ForegroundColor Cyan

    if (-not (Test-Path $artifactsDir)) {
        New-Item -ItemType Directory -Path $artifactsDir -Force | Out-Null
    }

    if (-not (Test-Path $issFile)) {
        Write-Error "Inno Setup script not found: $issFile"
        return
    }

    # Map architecture to Inno Setup values
    switch ($Architecture) {
        "x64"   { $innoArch = "x64compatible" }
        "arm64" { $innoArch = "arm64" }
    }

    $isccArgs = @(
        "/DAppVersion=$Version"
        "/DAppArchitecture=$innoArch"
        "/DPublishDir=$publishDir"
        "/DOutputDir=$artifactsDir"
        "/DInstallerName=$installerName"
        $issFile
    )

    Write-Host "  ISCC $($isccArgs -join ' ')" -ForegroundColor Gray
    & $isccExe @isccArgs

    if ($LASTEXITCODE -ne 0) {
        Write-Error "Inno Setup compilation failed with exit code $LASTEXITCODE"
        return
    }

    $installerPath = Join-Path $artifactsDir "$installerName.exe"
    Write-Host "  Installer created: $installerPath" -ForegroundColor Green
} else {
    Write-Host "[4/5] Skipping installer creation (--SkipInstaller)" -ForegroundColor Yellow
}
Write-Host ""

# --- Step 5: Sign installer ---
if (-not $SkipSign -and -not $SkipInstaller) {
    Write-Host "[5/5] Signing installer..." -ForegroundColor Cyan

    $installerPath = Join-Path $artifactsDir "$installerName.exe"
    if (Test-Path $installerPath) {
        $signArgs = @("sign", "/fd", "SHA256", "/td", "SHA256")

        if ($CertThumbprint) {
            $signArgs += @("/sha1", $CertThumbprint)
        } elseif ($CertPfxPath) {
            # Password already captured in step 3, but variable is out of scope
            # Re-prompt or reuse if stored
            if (-not $plainPassword) {
                $pfxPassword = Read-Host -Prompt "Enter PFX password for installer signing" -AsSecureString
                $bstr = [System.Runtime.InteropServices.Marshal]::SecureStringToBSTR($pfxPassword)
                $plainPassword = [System.Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr)
                [System.Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr)
            }
            $signArgs += @("/f", $CertPfxPath, "/p", $plainPassword)
        }

        $signArgs += @($installerPath)
        Write-Host "  Signing: $installerName.exe" -ForegroundColor Gray
        & $signtoolExe @signArgs 2>&1 | Out-Null
        if ($LASTEXITCODE -ne 0) {
            Write-Error "Failed to sign installer"
            return
        }
        Write-Host "  Installer signed" -ForegroundColor Green
    } else {
        Write-Warning "Installer not found at $installerPath — skipping signing"
    }
} else {
    Write-Host "[5/5] Skipping installer signing" -ForegroundColor Yellow
}
Write-Host ""

# --- Done ---
Write-Host "============================================" -ForegroundColor Green
Write-Host "  Build complete!" -ForegroundColor Green
Write-Host "============================================" -ForegroundColor Green
Write-Host "  Publish output: $publishDir" -ForegroundColor White
if (-not $SkipInstaller) {
    $installerPath = Join-Path $artifactsDir "$installerName.exe"
    Write-Host "  Installer:      $installerPath" -ForegroundColor White
}
Write-Host ""
if (-not $SkipInstaller) {
    Write-Host "  Silent install:   .\$installerName.exe /VERYSILENT /SUPPRESSMSGBOXES /NORESTART" -ForegroundColor Gray
    Write-Host "  Silent uninstall: `"C:\Program Files\OpenScribe\unins000.exe`" /VERYSILENT /SUPPRESSMSGBOXES /NORESTART" -ForegroundColor Gray
    Write-Host ""
}
