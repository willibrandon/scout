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

function Resolve-RepoRelativePath {
    param([Parameter(Mandatory = $true)][string] $Path)

    $fullPath = Resolve-RepoPath $Path
    $rootPrefix = $Root.TrimEnd(@([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar)) + [System.IO.Path]::DirectorySeparatorChar
    if (-not $fullPath.StartsWith($rootPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Oracle archive members must be under the repository root: $fullPath"
    }

    return $fullPath.Substring($rootPrefix.Length).Replace("\", "/")
}

function New-OracleArchive {
    param(
        [Parameter(Mandatory = $true)][string] $ArchivePath,
        [Parameter(Mandatory = $true)][string] $ReferenceEntryName,
        [Parameter(Mandatory = $true)][string] $ReferencePath,
        [Parameter(Mandatory = $true)][string] $Pcre2EntryName,
        [Parameter(Mandatory = $true)][string] $Pcre2Path
    )

    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $ArchivePath) | Out-Null
    Remove-Item -LiteralPath $ArchivePath -Force -ErrorAction SilentlyContinue

    $zip = [System.IO.Compression.ZipFile]::Open($ArchivePath, [System.IO.Compression.ZipArchiveMode]::Create)
    try {
        Add-OracleArchiveEntry $zip $ReferenceEntryName $ReferencePath
        Add-OracleArchiveEntry $zip $Pcre2EntryName $Pcre2Path
    }
    finally {
        $zip.Dispose()
    }
}

function Add-OracleArchiveEntry {
    param(
        [Parameter(Mandatory = $true)][System.IO.Compression.ZipArchive] $Zip,
        [Parameter(Mandatory = $true)][string] $EntryName,
        [Parameter(Mandatory = $true)][string] $Path
    )

    $entry = $Zip.CreateEntry($EntryName, [System.IO.Compression.CompressionLevel]::Optimal)
    $entry.LastWriteTime = [System.DateTimeOffset]::new(1980, 1, 1, 0, 0, 0, [System.TimeSpan]::Zero)
    $input = [System.IO.File]::OpenRead($Path)
    try {
        $output = $entry.Open()
        try {
            $input.CopyTo($output)
        }
        finally {
            $output.Dispose()
        }
    }
    finally {
        $input.Dispose()
    }
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
    $rowPathValue = if ([string]::IsNullOrWhiteSpace($env:SCOUT_RIPGREP_ROW_PATH)) {
        "tests/oracles/ripgrep/$HostRid.lock"
    } else {
        $env:SCOUT_RIPGREP_ROW_PATH
    }

    $rowPath = Resolve-RepoPath $rowPathValue
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $rowPath) | Out-Null
    $lines = @(
        "--- tests/PREREQS.lock ripgrep oracle row ---",
        "[[ripgrep_oracle]]",
        "rid = ""$HostRid""",
        "environment = ""$HostOracleEnvironment""",
        "archive_path = ""$OracleArchiveValue""",
        "archive_sha256 = ""$OracleArchiveSha256""",
        "profile = ""$RgProfile""",
        "path = ""$RgPathValue""",
        "sha256 = ""$RgSha256""",
        "pcre2_profile = ""$RgPcre2Profile""",
        "pcre2_features = ""$RgPcre2Features""",
        "pcre2_path = ""$RgPcre2PathValue""",
        "pcre2_sha256 = ""$RgPcre2Sha256""",
        "--- end ripgrep oracle row ---")
    [System.IO.File]::WriteAllLines($rowPath, $lines)
    $lines | ForEach-Object { Write-Output $_ }
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

$OracleArchiveValue = if ([string]::IsNullOrWhiteSpace($env:SCOUT_RIPGREP_ARCHIVE_PATH)) {
    "tests/oracles/ripgrep/$HostRid.zip"
} else {
    $env:SCOUT_RIPGREP_ARCHIVE_PATH
}

$RgPath = Resolve-RepoPath $RgPathValue
$RgPcre2Path = Resolve-RepoPath $RgPcre2PathValue
$OracleArchive = Resolve-RepoPath $OracleArchiveValue

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
New-OracleArchive `
    -ArchivePath $OracleArchive `
    -ReferenceEntryName (Resolve-RepoRelativePath $RgPathValue) `
    -ReferencePath $RgPath `
    -Pcre2EntryName (Resolve-RepoRelativePath $RgPcre2PathValue) `
    -Pcre2Path $RgPcre2Path
$OracleArchiveSha256 = Get-Sha256 $OracleArchive
Write-LockRow
