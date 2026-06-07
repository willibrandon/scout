param(
    [Parameter(Mandatory = $true)]
    [ValidateSet("win-x64", "win-arm64")]
    [string] $Rid
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$Root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$Bin = Join-Path $Root "artifacts\bin\$Rid"
$PackageRoot = Join-Path $Root "artifacts\packages"
$StageParent = Join-Path $PackageRoot "stage"
$Stage = Join-Path $StageParent "scout-$Rid"
$Archive = Join-Path $PackageRoot "scout-$Rid.zip"
$HashFile = "$Archive.sha256"
$BuildProps = [xml](Get-Content (Join-Path $Root "Directory.Build.props"))

function Read-MsBuildProperty {
    param([string] $Name)

    foreach ($propertyGroup in $BuildProps.Project.PropertyGroup) {
        $value = $propertyGroup.$Name
        if ($null -ne $value -and ([string] $value) -ne "") {
            return [string] $value
        }
    }

    throw "Missing MSBuild property $Name in Directory.Build.props."
}

function Require-File {
    param([string] $Path)

    if (-not (Test-Path -PathType Leaf $Path)) {
        throw "Missing required package input: $Path"
    }
}

$Exe = Join-Path $Bin "scout.exe"
Require-File $Exe
Require-File (Join-Path $Root "docs\PARITY.md")
Require-File (Join-Path $Root "docs\THIRD-PARTY-NOTICES.md")

$ScoutVersion = if ([string]::IsNullOrWhiteSpace($env:SCOUT_RELEASE_VERSION)) {
    Read-MsBuildProperty "VersionPrefix"
}
else {
    $env:SCOUT_RELEASE_VERSION
}

Remove-Item -Recurse -Force $Stage -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $Stage | Out-Null
Copy-Item $Exe (Join-Path $Stage "scout.exe")
Copy-Item (Join-Path $Root "docs\PARITY.md") (Join-Path $Stage "PARITY.md")
Copy-Item (Join-Path $Root "docs\THIRD-PARTY-NOTICES.md") (Join-Path $Stage "THIRD-PARTY-NOTICES.md")

$Manifest = @(
    'name = "Scout"',
    "version = `"$ScoutVersion`"",
    'binary = "scout.exe"',
    "rid = `"$Rid`"",
    'ripgrep_commit = "4857d6fa67db69a95cd4b6f2adda5d807d4d0119"',
    'dotnet_runtime = "10.0.2"',
    'pcre2 = "10.46"',
    'parity = "behavioral parity; identity is Scout-specific (see PARITY.md)"'
)
Set-Content -Encoding ascii -Path (Join-Path $Stage "SCOUT-PACKAGE.txt") -Value $Manifest

New-Item -ItemType Directory -Force -Path $PackageRoot | Out-Null
Remove-Item -Force $Archive, $HashFile -ErrorAction SilentlyContinue
Compress-Archive -Path $Stage -DestinationPath $Archive

$Hash = (Get-FileHash -Algorithm SHA256 -Path $Archive).Hash.ToLowerInvariant()
Set-Content -Encoding ascii -Path $HashFile -Value "$Hash  $(Split-Path -Leaf $Archive)"
Write-Host "OK ${Rid}: release package written to $Archive"
