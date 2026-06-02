Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$Root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$Lock = Join-Path $Root "tests\PREREQS.lock"

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

function Get-HostRid {
    if (-not [System.OperatingSystem]::IsWindows()) {
        throw "Windows ripgrep oracle capture must run on Windows."
    }

    $architecture = switch ([System.Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture) {
        ([System.Runtime.InteropServices.Architecture]::X64) { "x64"; break }
        ([System.Runtime.InteropServices.Architecture]::Arm64) { "arm64"; break }
        default { throw "Unsupported process architecture for ripgrep oracle capture: $([System.Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture)" }
    }

    return "win-$architecture"
}

function Get-OracleEnvironment {
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

    & git -C $Reference fetch --depth 1 origin $ExpectedCommit
    if ($LASTEXITCODE -ne 0) {
        Invoke-Checked git -C $Reference fetch origin
    }

    Invoke-Checked git -C $Reference checkout --detach $ExpectedCommit
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

function Write-LockRow {
    Write-Output "--- tests/PREREQS.lock ripgrep oracle row ---"
    Write-Output "[[ripgrep_oracle]]"
    Write-Output "rid = ""$HostRid"""
    Write-Output "environment = ""$HostOracleEnvironment"""
    Write-Output "profile = ""$RgProfile"""
    Write-Output "path = ""$RgPathValue"""
    Write-Output "sha256 = ""$RgSha256"""
    Write-Output "pcre2_profile = ""$RgPcre2Profile"""
    Write-Output "pcre2_features = ""$RgPcre2Features"""
    Write-Output "pcre2_path = ""$RgPcre2PathValue"""
    Write-Output "pcre2_sha256 = ""$RgPcre2Sha256"""
    Write-Output "--- end ripgrep oracle row ---"
}

$ExpectedRipgrep = Read-LockValue "ripgrep_commit"
$RustToolchain = Read-LockValue "cargo"
$RgProfile = Read-LockValue "ripgrep_rg_profile"
$RgPcre2Profile = Read-LockValue "ripgrep_pcre2_rg_profile"
$RgPcre2Features = Read-LockValue "ripgrep_pcre2_rg_features"
$HostRid = Get-HostRid
$HostOracleEnvironment = Get-OracleEnvironment

$referenceValue = if ([string]::IsNullOrWhiteSpace($env:SCOUT_RIPGREP_REFERENCE)) {
    "artifacts/ripgrep-oracle/$HostRid/ripgrep"
} else {
    $env:SCOUT_RIPGREP_REFERENCE
}

$Reference = Resolve-RepoPath $referenceValue
$RgPathValue = if ([string]::IsNullOrWhiteSpace($env:SCOUT_RIPGREP_RG_PATH)) {
    "$referenceValue/target/$RgProfile/rg.exe"
} else {
    $env:SCOUT_RIPGREP_RG_PATH
}

$RgPcre2PathValue = if ([string]::IsNullOrWhiteSpace($env:SCOUT_RIPGREP_PCRE2_RG_PATH)) {
    "$referenceValue/target/pcre2/$RgPcre2Profile/rg.exe"
} else {
    $env:SCOUT_RIPGREP_PCRE2_RG_PATH
}

$RgPath = Resolve-RepoPath $RgPathValue
$RgPcre2Path = Resolve-RepoPath $RgPcre2PathValue

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

Build-Ripgrep
if (-not (Test-Path $RgPath)) {
    throw "Missing built reference rg: $RgPath"
}

$RgSha256 = Get-Sha256 $RgPath

Build-Pcre2Ripgrep
if (-not (Test-Path $RgPcre2Path)) {
    throw "Missing built PCRE2 reference rg: $RgPcre2Path"
}

$RgPcre2Sha256 = Get-Sha256 $RgPcre2Path
Write-LockRow
