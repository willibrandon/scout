param(
    [Parameter(Mandatory = $true)]
    [ValidateSet("win-x64", "win-arm64")]
    [string] $Rid
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$Root = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$Source = Join-Path $Root "native\pcre2\pcre2-10.46"
$Out = Join-Path $Root "artifacts\native\pcre2\$Rid"
$Obj = Join-Path $Out "obj"
$Lib = Join-Path $Out "lib"
$Include = Join-Path $Out "include"

function Require-Tool {
    param([string] $Name)

    if ($null -eq (Get-Command $Name -ErrorAction SilentlyContinue)) {
        throw "$Name was not found. Run this script from a Visual Studio Developer command prompt."
    }
}

Require-Tool "cl.exe"
Require-Tool "lib.exe"

if (Test-Path $Out) {
    Remove-Item -Recurse -Force $Out
}

New-Item -ItemType Directory -Force -Path $Obj, $Lib, $Include | Out-Null

$CommonArgs = @(
    "/nologo",
    "/O2",
    "/TC",
    "/DPCRE2_CODE_UNIT_WIDTH=8",
    "/DHAVE_STDLIB_H=1",
    "/DHAVE_MEMMOVE=1",
    "/DHAVE_WINDOWS_H=1",
    "/DHAVE_CONFIG_H=1",
    "/DPCRE2_STATIC=1",
    "/DSTDC_HEADERS=1",
    "/DSUPPORT_PCRE2_8=1",
    "/DSUPPORT_UNICODE=1",
    "/DSUPPORT_JIT=1",
    "/I", (Join-Path $Source "src"),
    "/I", (Join-Path $Source "deps"),
    "/I", (Join-Path $Source "include")
)

$ExcludedSources = @(
    "pcre2_jit_match.c",
    "pcre2_jit_misc.c",
    "pcre2_ucptables.c"
)

$Objects = @()
foreach ($SourceFile in Get-ChildItem -Path (Join-Path $Source "src") -Filter "*.c") {
    if ($ExcludedSources -contains $SourceFile.Name) {
        continue
    }

    $ObjectPath = Join-Path $Obj ($SourceFile.BaseName + ".obj")
    & cl.exe @CommonArgs "/Fo$ObjectPath" /c $SourceFile.FullName
    if ($LASTEXITCODE -ne 0) {
        throw "cl.exe failed for $($SourceFile.FullName)."
    }

    $Objects += $ObjectPath
}

$LibraryPath = Join-Path $Lib "pcre2-8.lib"
& lib.exe /NOLOGO "/OUT:$LibraryPath" @Objects
if ($LASTEXITCODE -ne 0) {
    throw "lib.exe failed for $LibraryPath."
}

Copy-Item (Join-Path $Source "include\pcre2.h") (Join-Path $Include "pcre2.h")
Write-Host "built $LibraryPath"
