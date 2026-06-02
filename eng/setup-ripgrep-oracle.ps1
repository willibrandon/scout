Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$Root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$Lock = Join-Path $Root "tests\PREREQS.lock"
$DefaultReference = "C:\scout-ripgrep"

function Read-LockValue {
    param([Parameter(Mandatory = $true)][string] $Key)

    $prefix = "$Key = """
    foreach ($line in [System.IO.File]::ReadLines($Lock)) {
        if ($line.StartsWith("[", [System.StringComparison]::Ordinal)) {
            break
        }

        if ($line.StartsWith($prefix, [System.StringComparison]::Ordinal) -and $line.EndsWith("""", [System.StringComparison]::Ordinal)) {
            return $line.Substring($prefix.Length, $line.Length - $prefix.Length - 1)
        }
    }

    throw "Missing $Key in tests/PREREQS.lock."
}

function Read-LockRidTableValue {
    param(
        [Parameter(Mandatory = $true)][string] $Table,
        [Parameter(Mandatory = $true)][string] $Rid,
        [string] $Environment,
        [Parameter(Mandatory = $true)][string] $Key
    )

    $header = "[[$Table]]"
    $inTable = $false
    $matchedRid = $false
    $matchedEnvironment = [string]::IsNullOrEmpty($Environment)
    $value = ""
    foreach ($line in [System.IO.File]::ReadLines($Lock)) {
        $trimmed = $line.Trim()
        if ($trimmed -eq $header) {
            $inTable = $true
            $matchedRid = $false
            $matchedEnvironment = [string]::IsNullOrEmpty($Environment)
            continue
        }

        if ($inTable -and $trimmed.StartsWith("[", [System.StringComparison]::Ordinal)) {
            $inTable = $false
            $matchedRid = $false
            $matchedEnvironment = [string]::IsNullOrEmpty($Environment)
        }

        if (-not $inTable) {
            continue
        }

        if ($trimmed.Length -eq 0) {
            continue
        }

        if (Try-ReadAssignment $trimmed "rid" ([ref] $value)) {
            $matchedRid = [string]::Equals($value, $Rid, [System.StringComparison]::Ordinal)
            continue
        }

        if (Try-ReadAssignment $trimmed "environment" ([ref] $value)) {
            $matchedEnvironment = -not [string]::IsNullOrEmpty($Environment) -and [string]::Equals($value, $Environment, [System.StringComparison]::Ordinal)
            continue
        }

        if ($matchedRid -and $matchedEnvironment -and (Try-ReadAssignment $trimmed $Key ([ref] $value))) {
            return $value
        }
    }

    throw "Missing $Table.$Key for $Rid in tests/PREREQS.lock."
}

function Read-LockRidTableValueAnyEnvironment {
    param(
        [Parameter(Mandatory = $true)][string] $Table,
        [Parameter(Mandatory = $true)][string] $Rid,
        [Parameter(Mandatory = $true)][string] $Key
    )

    $header = "[[$Table]]"
    $inTable = $false
    $matchedRid = $false
    $value = ""
    $matches = [System.Collections.Generic.List[string]]::new()
    foreach ($line in [System.IO.File]::ReadLines($Lock)) {
        $trimmed = $line.Trim()
        if ($trimmed -eq $header) {
            $inTable = $true
            $matchedRid = $false
            continue
        }

        if ($inTable -and $trimmed.StartsWith("[", [System.StringComparison]::Ordinal)) {
            $inTable = $false
            $matchedRid = $false
        }

        if (-not $inTable) {
            continue
        }

        if ($trimmed.Length -eq 0) {
            continue
        }

        if (Try-ReadAssignment $trimmed "rid" ([ref] $value)) {
            $matchedRid = [string]::Equals($value, $Rid, [System.StringComparison]::Ordinal)
            continue
        }

        if ($matchedRid -and (Try-ReadAssignment $trimmed $Key ([ref] $value))) {
            $matches.Add($value)
            continue
        }
    }

    if ($matches.Count -eq 1) {
        return $matches[0]
    }

    if ($matches.Count -gt 1) {
        throw "Multiple $Table.$Key rows for $Rid in tests/PREREQS.lock; set SCOUT_ORACLE_ENVIRONMENT to disambiguate."
    }

    throw "Missing $Table.$Key for $Rid in tests/PREREQS.lock."
}

function Try-ReadAssignment {
    param(
        [Parameter(Mandatory = $true)][string] $Line,
        [Parameter(Mandatory = $true)][string] $Key,
        [Parameter(Mandatory = $true)][ref] $Value
    )

    $prefix = "$Key = """
    if ($Line.StartsWith($prefix, [System.StringComparison]::Ordinal) -and $Line.EndsWith("""", [System.StringComparison]::Ordinal)) {
        $Value.Value = $Line.Substring($prefix.Length, $Line.Length - $prefix.Length - 1)
        return $true
    }

    $Value.Value = ""
    return $false
}

function Test-LockHasTable {
    param([Parameter(Mandatory = $true)][string] $Table)

    $header = "[[$Table]]"
    foreach ($line in [System.IO.File]::ReadLines($Lock)) {
        if ($line.Trim() -eq $header) {
            return $true
        }
    }

    return $false
}

function Read-OracleValue {
    param(
        [Parameter(Mandatory = $true)][string] $TableKey,
        [Parameter(Mandatory = $true)][string] $RootKey
    )

    try {
        return Read-LockRidTableValue "ripgrep_oracle" $HostRid $HostOracleEnvironment $TableKey
    }
    catch {
        try {
            return Read-LockRidTableValue "ripgrep_oracle" $HostRid "" $TableKey
        }
        catch {
            try {
                return Read-LockRidTableValueAnyEnvironment "ripgrep_oracle" $HostRid $TableKey
            }
            catch {
                if (Test-LockHasTable "ripgrep_oracle") {
                    throw "Could not find ripgrep_oracle.$TableKey for host RID $HostRid in tests/PREREQS.lock."
                }
            }
        }
    }

    return Read-LockValue $RootKey
}

function Get-HostRid {
    if (-not [string]::IsNullOrWhiteSpace($env:SCOUT_HOST_RID)) {
        switch ($env:SCOUT_HOST_RID) {
            "win-x64" { return "win-x64" }
            "win-arm64" { return "win-arm64" }
            default { throw "Unsupported SCOUT_HOST_RID for Windows pinned ripgrep oracle: $env:SCOUT_HOST_RID" }
        }
    }

    if (-not [System.OperatingSystem]::IsWindows()) {
        throw "Windows pinned ripgrep oracle setup must run on Windows."
    }

    $architecture = switch ([System.Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture) {
        ([System.Runtime.InteropServices.Architecture]::X64) { "x64"; break }
        ([System.Runtime.InteropServices.Architecture]::Arm64) { "arm64"; break }
        default { throw "Unsupported process architecture for pinned ripgrep oracle: $([System.Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture)" }
    }

    return "win-$architecture"
}

function Get-OracleEnvironment {
    if (-not [string]::IsNullOrWhiteSpace($env:SCOUT_ORACLE_ENVIRONMENT)) {
        switch ($env:SCOUT_ORACLE_ENVIRONMENT) {
            "github-actions" { return "github-actions" }
            "local" { return "local" }
            default { throw "Unsupported SCOUT_ORACLE_ENVIRONMENT: $env:SCOUT_ORACLE_ENVIRONMENT" }
        }
    }

    if ([string]::Equals($env:GITHUB_ACTIONS, "true", [System.StringComparison]::OrdinalIgnoreCase)) {
        return "github-actions"
    }

    return "local"
}

function Resolve-RepoPath {
    param([Parameter(Mandatory = $true)][string] $Path)

    if ([System.IO.Path]::IsPathRooted($Path)) {
        return [System.IO.Path]::GetFullPath($Path)
    }

    return [System.IO.Path]::GetFullPath((Join-Path $Root $Path))
}

function Derive-ReferenceFromOraclePath {
    param([Parameter(Mandatory = $true)][string] $ExecutablePath)

    $fullPath = [System.IO.Path]::GetFullPath($ExecutablePath)
    $normalized = $fullPath.Replace("\", "/")
    $targetIndex = $normalized.IndexOf("/target/", [System.StringComparison]::Ordinal)
    if ($targetIndex -lt 0) {
        return $DefaultReference
    }

    return $fullPath.Substring(0, $targetIndex)
}

function Get-Sha256 {
    param([Parameter(Mandatory = $true)][string] $Path)

    $hash = [System.Security.Cryptography.SHA256]::HashData([System.IO.File]::ReadAllBytes($Path))
    return [System.Convert]::ToHexString($hash).ToLowerInvariant()
}

function Invoke-Checked {
    param(
        [Parameter(Mandatory = $true)][string] $FilePath,
        [Parameter(ValueFromRemainingArguments = $true)][string[]] $Arguments
    )

    & $FilePath @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$FilePath failed with exit code $LASTEXITCODE."
    }
}

function Ensure-Rustup {
    if ($null -ne (Get-Command rustup -ErrorAction SilentlyContinue)) {
        return
    }

    $installer = Join-Path ([System.IO.Path]::GetTempPath()) ("rustup-init-" + [System.Guid]::NewGuid().ToString("N") + ".exe")
    Invoke-WebRequest -Uri "https://win.rustup.rs/x86_64" -OutFile $installer
    try {
        Invoke-Checked $installer -y --profile minimal --default-toolchain none
    }
    finally {
        Remove-Item -LiteralPath $installer -Force -ErrorAction SilentlyContinue
    }

    $cargoBin = Join-Path $env:USERPROFILE ".cargo\bin"
    if ($env:PATH -notlike "*$cargoBin*") {
        $env:PATH = "$cargoBin;$env:PATH"
    }
}

function Ensure-ReferenceCheckout {
    param([Parameter(Mandatory = $true)][string] $ExpectedCommit)

    if (-not (Test-Path (Join-Path $Reference ".git"))) {
        New-Item -ItemType Directory -Force -Path (Split-Path -Parent $Reference) | Out-Null
        Remove-Item -LiteralPath $Reference -Recurse -Force -ErrorAction SilentlyContinue
        Invoke-Checked git init $Reference
        Invoke-Checked git -C $Reference remote add origin https://github.com/BurntSushi/ripgrep.git
    }

    $actualCommit = (& git -C $Reference rev-parse HEAD 2>$null)
    if ($LASTEXITCODE -eq 0 -and $actualCommit -eq $ExpectedCommit) {
        return
    }

    if (-not [string]::IsNullOrEmpty($actualCommit) -and $env:CI -ne "true") {
        throw "$Reference is at $actualCommit, expected $ExpectedCommit. Move or update the reference checkout explicitly before running this setup locally."
    }

    & git -C $Reference fetch --depth 1 origin $ExpectedCommit
    if ($LASTEXITCODE -ne 0) {
        Invoke-Checked git -C $Reference fetch origin
    }

    Invoke-Checked git -C $Reference checkout --detach $ExpectedCommit
}

function Set-ReproducibleWindowsRustBuildEnvironment {
    $sourceDateEpoch = (& git -C $Reference show -s --format=%ct HEAD)
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($sourceDateEpoch)) {
        throw "Could not read pinned ripgrep commit timestamp for reproducible Windows Rust build."
    }

    $env:CARGO_INCREMENTAL = "0"
    $env:SOURCE_DATE_EPOCH = $sourceDateEpoch.Trim()
    $breproFlag = "-C link-arg=/Brepro"
    if ([string]::IsNullOrWhiteSpace($env:RUSTFLAGS)) {
        $env:RUSTFLAGS = $breproFlag
    } elseif ($env:RUSTFLAGS -notlike "*/Brepro*") {
        $env:RUSTFLAGS = ($env:RUSTFLAGS.Trim() + " " + $breproFlag).Trim()
    }

    Write-Host "Using reproducible Windows Rust build flags: SOURCE_DATE_EPOCH=$env:SOURCE_DATE_EPOCH RUSTFLAGS=$env:RUSTFLAGS."
}

function Test-HashMatches {
    param(
        [Parameter(Mandatory = $true)][string] $Path,
        [Parameter(Mandatory = $true)][string] $ExpectedSha256
    )

    return (Test-Path $Path) -and ((Get-Sha256 $Path) -eq $ExpectedSha256)
}

function Assert-BinaryHash {
    param(
        [Parameter(Mandatory = $true)][string] $Label,
        [Parameter(Mandatory = $true)][string] $Path,
        [Parameter(Mandatory = $true)][string] $ExpectedSha256
    )

    if (-not (Test-Path $Path)) {
        throw "Missing executable ${Label}: $Path"
    }

    $actualSha256 = Get-Sha256 $Path
    if ($actualSha256 -ne $ExpectedSha256) {
        throw "Expected $Label sha256 $ExpectedSha256, got $actualSha256."
    }
}

function Build-Ripgrep {
    Push-Location $Reference
    try {
        Invoke-Checked cargo "+$RustToolchain" build --profile $RgProfile --bin rg
    }
    finally {
        Pop-Location
    }
}

function Build-Pcre2Ripgrep {
    $previousTargetDirectory = $env:CARGO_TARGET_DIR
    $previousStatic = $env:PCRE2_SYS_STATIC
    $env:CARGO_TARGET_DIR = Join-Path $Reference "target\pcre2"
    $env:PCRE2_SYS_STATIC = "1"
    Push-Location $Reference
    try {
        Invoke-Checked cargo "+$RustToolchain" build --profile $RgPcre2Profile --features $RgPcre2Features --bin rg
    }
    finally {
        Pop-Location
        $env:CARGO_TARGET_DIR = $previousTargetDirectory
        $env:PCRE2_SYS_STATIC = $previousStatic
    }
}

$ExpectedRipgrep = Read-LockValue "ripgrep_commit"
$RustToolchain = Read-LockValue "cargo"
$HostRid = Get-HostRid
$HostOracleEnvironment = Get-OracleEnvironment
Write-Host "Using ripgrep oracle host RID $HostRid ($HostOracleEnvironment)."
$RgProfile = Read-OracleValue "profile" "ripgrep_rg_profile"
$RgPath = Resolve-RepoPath (Read-OracleValue "path" "ripgrep_rg_path")
$RgSha256 = Read-OracleValue "sha256" "ripgrep_rg_sha256"
$RgPcre2Profile = Read-OracleValue "pcre2_profile" "ripgrep_pcre2_rg_profile"
$RgPcre2Features = Read-OracleValue "pcre2_features" "ripgrep_pcre2_rg_features"
$RgPcre2Path = Resolve-RepoPath (Read-OracleValue "pcre2_path" "ripgrep_pcre2_rg_path")
$RgPcre2Sha256 = Read-OracleValue "pcre2_sha256" "ripgrep_pcre2_rg_sha256"
$Reference = Derive-ReferenceFromOraclePath $RgPath

if (-not $RgPath.EndsWith("rg.exe", [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Windows reference rg path must end with rg.exe: $RgPath"
}

if (-not $RgPcre2Path.EndsWith("rg.exe", [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Windows PCRE2 reference rg path must end with rg.exe: $RgPcre2Path"
}

Ensure-Rustup
Invoke-Checked rustup toolchain install $RustToolchain --profile minimal
$actualCargo = (& cargo "+$RustToolchain" --version).Split(" ")[1]
if ($actualCargo -ne $RustToolchain) {
    throw "Expected cargo $RustToolchain, got $actualCargo."
}

Ensure-ReferenceCheckout $ExpectedRipgrep
$actualRipgrep = (& git -C $Reference rev-parse HEAD)
if ($actualRipgrep -ne $ExpectedRipgrep) {
    throw "Expected ripgrep commit $ExpectedRipgrep, got $actualRipgrep."
}

Set-ReproducibleWindowsRustBuildEnvironment

if (-not (Test-HashMatches $RgPath $RgSha256)) {
    Build-Ripgrep
}

Assert-BinaryHash "reference rg" $RgPath $RgSha256

if (-not (Test-HashMatches $RgPcre2Path $RgPcre2Sha256)) {
    Build-Pcre2Ripgrep
}

Assert-BinaryHash "PCRE2 reference rg" $RgPcre2Path $RgPcre2Sha256
Write-Host "OK pinned ripgrep oracle is ready at $Reference"
