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

        if (-not $inTable -or $trimmed.Length -eq 0) {
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

function Read-OracleValue {
    param([Parameter(Mandatory = $true)][string] $TableKey)

    try {
        return Read-LockRidTableValue "ripgrep_oracle" $HostRid $HostOracleEnvironment $TableKey
    }
    catch {
        return Read-LockRidTableValue "ripgrep_oracle" $HostRid "" $TableKey
    }
}

function Get-HostRid {
    if (-not [string]::IsNullOrWhiteSpace($env:SCOUT_HOST_RID)) {
        switch ($env:SCOUT_HOST_RID) {
            "win-x64" { return "win-x64" }
            "win-arm64" { return "win-arm64" }
            default { throw "Unsupported SCOUT_HOST_RID for Windows pinned ripgrep oracle archive: $env:SCOUT_HOST_RID" }
        }
    }

    if (-not [System.OperatingSystem]::IsWindows()) {
        throw "Windows pinned ripgrep oracle archive restore must run on Windows."
    }

    $architecture = switch ([System.Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture) {
        ([System.Runtime.InteropServices.Architecture]::X64) { "x64"; break }
        ([System.Runtime.InteropServices.Architecture]::Arm64) { "arm64"; break }
        default { throw "Unsupported process architecture for pinned ripgrep oracle archive: $([System.Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture)" }
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

function Get-Sha256 {
    param([Parameter(Mandatory = $true)][string] $Path)

    $hash = [System.Security.Cryptography.SHA256]::HashData([System.IO.File]::ReadAllBytes($Path))
    return [System.Convert]::ToHexString($hash).ToLowerInvariant()
}

function Assert-LowercaseSha256 {
    param(
        [Parameter(Mandatory = $true)][string] $Label,
        [Parameter(Mandatory = $true)][string] $Value
    )

    if ($Value.Length -ne 64 -or $Value -cmatch "[^0-9a-f]") {
        throw "$Label must be a literal lowercase SHA-256 in tests/PREREQS.lock: $Value"
    }
}

function Assert-FileHash {
    param(
        [Parameter(Mandatory = $true)][string] $Label,
        [Parameter(Mandatory = $true)][string] $Path,
        [Parameter(Mandatory = $true)][string] $ExpectedSha256
    )

    if (-not (Test-Path $Path)) {
        throw "Missing ${Label}: $Path"
    }

    $actualSha256 = Get-Sha256 $Path
    if ($actualSha256 -ne $ExpectedSha256) {
        throw "Expected $Label sha256 $ExpectedSha256, got $actualSha256."
    }
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

$HostRid = Get-HostRid
$HostOracleEnvironment = Get-OracleEnvironment
Write-Host "Using ripgrep oracle archive host RID $HostRid ($HostOracleEnvironment)."
$ExpectedRipgrep = Read-LockValue "ripgrep_commit"
$ExpectedPcre2Version = Read-LockValue "ripgrep_pcre2_reported_version"
$ArchivePathValue = Read-OracleValue "archive_path"
$ArchiveSha256 = Read-OracleValue "archive_sha256"
$RgPath = Resolve-RepoPath (Read-OracleValue "path")
$RgSha256 = Read-OracleValue "sha256"
$RgPcre2Path = Resolve-RepoPath (Read-OracleValue "pcre2_path")
$RgPcre2Sha256 = Read-OracleValue "pcre2_sha256"
$ArchivePath = Resolve-RepoPath $ArchivePathValue

Assert-LowercaseSha256 "ripgrep_oracle.archive_sha256" $ArchiveSha256
Assert-LowercaseSha256 "ripgrep_oracle.sha256" $RgSha256
Assert-LowercaseSha256 "ripgrep_oracle.pcre2_sha256" $RgPcre2Sha256
Assert-FileHash "pinned ripgrep oracle archive" $ArchivePath $ArchiveSha256

Remove-Item -LiteralPath $RgPath, $RgPcre2Path -Force -ErrorAction SilentlyContinue
Expand-Archive -LiteralPath $ArchivePath -DestinationPath $Root -Force

Assert-BinaryHash "reference rg" $RgPath $RgSha256
Assert-BinaryHash "PCRE2 reference rg" $RgPcre2Path $RgPcre2Sha256

$expectedRevision = $ExpectedRipgrep.Substring(0, 10)
$actualVersion = (& $RgPath --version | Select-Object -First 1)
if (-not $actualVersion.Contains("rev $expectedRevision", [System.StringComparison]::Ordinal)) {
    throw "Expected reference rg revision $expectedRevision, got: $actualVersion"
}

$actualPcre2Version = (& $RgPcre2Path --pcre2-version).Trim()
if ($actualPcre2Version -ne $ExpectedPcre2Version) {
    throw "Expected PCRE2 reported version $ExpectedPcre2Version, got $actualPcre2Version."
}

Write-Host "OK pinned ripgrep oracle archive restored for $HostRid from $ArchivePathValue"
