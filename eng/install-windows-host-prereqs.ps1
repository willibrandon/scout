Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if (-not $IsWindows) {
    throw "Windows host prerequisites must run on Windows."
}

$requiredCommands = @(
    "gzip",
    "bzip2",
    "xz",
    "lz4",
    "brotli",
    "zstd",
    "compress",
    "uncompress"
)

function Add-PathEntry {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Path
    )

    if (-not (Test-Path -LiteralPath $Path -PathType Container)) {
        return
    }

    $separator = [System.IO.Path]::PathSeparator
    $entries = $env:PATH -split [regex]::Escape([string] $separator)
    if ($entries -notcontains $Path) {
        $env:PATH = $Path + $separator + $env:PATH
    }

    if (-not [string]::IsNullOrWhiteSpace($env:GITHUB_PATH)) {
        Add-Content -LiteralPath $env:GITHUB_PATH -Value $Path -Encoding utf8
    }
}

function Test-CommandAvailable {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Name
    )

    return $null -ne (Get-Command $Name -ErrorAction SilentlyContinue)
}

function Get-MissingCommands {
    return @($requiredCommands | Where-Object { -not (Test-CommandAvailable $_) })
}

function Get-Chocolatey {
    return Get-Command choco -ErrorAction SilentlyContinue
}

Add-PathEntry "C:\msys64\usr\bin"
Add-PathEntry "C:\ProgramData\chocolatey\bin"

$missing = Get-MissingCommands
if ($missing.Count -gt 0) {
    $pacman = "C:\msys64\usr\bin\pacman.exe"
    if (-not (Test-Path -LiteralPath $pacman -PathType Leaf)) {
        $choco = Get-Chocolatey
        if ($null -eq $choco) {
            throw "Missing MSYS2 pacman and Chocolatey; cannot install Windows host prerequisites."
        }

        & $choco.Source install msys2 --no-progress -y
    }

    if (Test-Path -LiteralPath $pacman -PathType Leaf) {
        & $pacman -Sy --noconfirm --needed gzip bzip2 xz lz4 brotli zstd ncompress
        Add-PathEntry "C:\msys64\usr\bin"
    }
}

$missing = Get-MissingCommands
$chocolateyPackages = @()
if ($missing -contains "lz4") {
    $chocolateyPackages += "lz4"
}

if ($missing -contains "brotli") {
    $chocolateyPackages += "brotli"
}

if ($missing -contains "zstd") {
    $chocolateyPackages += "zstandard"
}

if ($chocolateyPackages.Count -gt 0) {
    $choco = Get-Chocolatey
    if ($null -eq $choco) {
        throw "Chocolatey is required to install: $($chocolateyPackages -join ', ')"
    }

    & $choco.Source install @chocolateyPackages --no-progress -y
    Add-PathEntry "C:\ProgramData\chocolatey\bin"
}

$missing = Get-MissingCommands
if ($missing.Count -gt 0) {
    throw "Missing Windows host prerequisite command(s): $($missing -join ', ')"
}

foreach ($command in $requiredCommands) {
    $resolved = (Get-Command $command -ErrorAction Stop).Source
    Write-Host "$command -> $resolved"
}
