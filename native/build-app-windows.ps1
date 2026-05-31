param(
    [Parameter(Mandatory = $true)]
    [ValidateSet("win-x64", "win-arm64")]
    [string] $Rid
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$Root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$Out = Join-Path $Root "artifacts\app\$Rid"
$Bin = Join-Path $Root "artifacts\bin\$Rid"
$Pcre2Lib = Join-Path $Root "artifacts\native\pcre2\$Rid\lib\pcre2-8.lib"
$Runtime = Join-Path $env:USERPROFILE ".nuget\packages\microsoft.netcore.app.runtime.nativeaot.$Rid\10.0.2\runtimes\$Rid\native"

Push-Location $Root
try {

function Require-Tool {
    param([string] $Name)

    if ($null -eq (Get-Command $Name -ErrorAction SilentlyContinue)) {
        throw "$Name was not found. Run this script from a Visual Studio Developer command prompt."
    }
}

function Require-Path {
    param([string] $Path)

    if (-not (Test-Path $Path)) {
        throw "Missing required file: $Path"
    }

    return $Path
}

function Add-IfExists {
    param(
        [System.Collections.Generic.List[string]] $Items,
        [string] $Path
    )

    if (Test-Path $Path) {
        $Items.Add($Path)
    }
}

Require-Tool "cl.exe"
Require-Tool "link.exe"

& (Join-Path $Root "native\pcre2\build-windows.ps1") $Rid
dotnet publish (Join-Path $Root "src\Scout.App\Scout.App.csproj") -r $Rid -c Release -p:NativeLib=Static -o $Out
New-Item -ItemType Directory -Force -Path $Bin | Out-Null

$DriverObject = Join-Path $Out "scout_wmain.obj"
& cl.exe /nologo /O2 /TC /c (Join-Path $Root "native\entry\scout_wmain.c") "/Fo$DriverObject"
if ($LASTEXITCODE -ne 0) {
    throw "cl.exe failed for scout_wmain.c."
}

$Libraries = [System.Collections.Generic.List[string]]::new()
$Libraries.Add((Require-Path (Join-Path $Out "scout.lib")))
$Libraries.Add((Require-Path (Join-Path $Runtime "dllmain.obj")))
$Libraries.Add((Require-Path (Join-Path $Runtime "bootstrapperdll.obj")))
$Libraries.Add((Require-Path (Join-Path $Runtime "Runtime.WorkstationGC.lib")))
if ($Rid -eq "win-x64") {
    Add-IfExists $Libraries (Join-Path $Runtime "Runtime.VxsortEnabled.lib")
    Add-IfExists $Libraries (Join-Path $Runtime "Runtime.VxsortDisabled.lib")
}

$Libraries.Add((Require-Path (Join-Path $Runtime "eventpipe-disabled.lib")))
$Libraries.Add((Require-Path (Join-Path $Runtime "standalonegc-disabled.lib")))
$Libraries.Add((Require-Path (Join-Path $Runtime "aotminipal.lib")))
$Libraries.Add((Require-Path (Join-Path $Runtime "zlibstatic.lib")))
$Libraries.Add((Require-Path (Join-Path $Runtime "brotlicommon.lib")))
$Libraries.Add((Require-Path (Join-Path $Runtime "brotlienc.lib")))
$Libraries.Add((Require-Path (Join-Path $Runtime "brotlidec.lib")))
$Libraries.Add((Require-Path (Join-Path $Runtime "System.Globalization.Native.Aot.lib")))
$Libraries.Add((Require-Path (Join-Path $Runtime "System.IO.Compression.Native.Aot.lib")))

$OutputExe = Join-Path $Bin "scout.exe"
$SdkLibraries = @(
    "advapi32.lib",
    "bcrypt.lib",
    "crypt32.lib",
    "iphlpapi.lib",
    "kernel32.lib",
    "mswsock.lib",
    "ncrypt.lib",
    "normaliz.lib",
    "ntdll.lib",
    "ole32.lib",
    "oleaut32.lib",
    "secur32.lib",
    "shell32.lib",
    "user32.lib",
    "version.lib",
    "ws2_32.lib"
)

& link.exe /NOLOGO /MANIFEST:NO /SUBSYSTEM:CONSOLE /ENTRY:wmainCRTStartup /INCREMENTAL:NO /MERGE:.managedcode=.text /MERGE:hydrated=.bss /OPT:REF /OPT:ICF /NODEFAULTLIB:libucrt.lib /DEFAULTLIB:ucrt.lib "/OUT:$OutputExe" $DriverObject @Libraries "/WHOLEARCHIVE:$Pcre2Lib" @SdkLibraries
if ($LASTEXITCODE -ne 0) {
    throw "link.exe failed for $OutputExe."
}

$VersionOutput = @(& $OutputExe -V)
if ($LASTEXITCODE -ne 0 -or $VersionOutput.Count -ne 1 -or $VersionOutput[0] -ne "ripgrep 15.1.0 (rev 4857d6fa67)") {
    throw "Unexpected scout -V output: $VersionOutput"
}

$Pcre2VersionOutput = @(& $OutputExe --pcre2-version)
if ($LASTEXITCODE -ne 0 -or $Pcre2VersionOutput.Count -ne 1 -or $Pcre2VersionOutput[0] -ne "PCRE2 10.46 is available (JIT is available)") {
    throw "Unexpected scout --pcre2-version output: $Pcre2VersionOutput"
}

$SmokePath = Join-Path $Bin "pcre2-smoke.txt"
Set-Content -NoNewline -Encoding ascii -Path $SmokePath -Value "foobar`nfoo`nfoobarfoo`n"
$SmokeOutput = @(& $OutputExe -P "foo(?=bar)" $SmokePath)
if ($LASTEXITCODE -ne 0 -or ($SmokeOutput -join "`n") -ne "foobar`nfoobarfoo") {
    throw "Unexpected PCRE2 smoke output: $SmokeOutput"
}

$SmokePath2 = Join-Path $Bin "pcre2-smoke-2.txt"
Set-Content -NoNewline -Encoding ascii -Path $SmokePath2 -Value "barfoo`nfoobar`n"
$MultiOutput = @(& $OutputExe -P -n "foo(?=bar)" $SmokePath $SmokePath2)
$ExpectedMultiOutput = @(
    "${SmokePath}:1:foobar",
    "${SmokePath}:3:foobarfoo",
    "${SmokePath2}:2:foobar"
)
if ($LASTEXITCODE -ne 0 -or ($MultiOutput -join "`n") -ne ($ExpectedMultiOutput -join "`n")) {
    throw "Unexpected PCRE2 multi-file output: $MultiOutput"
}

Write-Host "OK ${Rid}: Scout.App native Windows export linked with PCRE2 and smoke checks passed"
}
finally {
    Pop-Location
}
