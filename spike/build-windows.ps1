param(
    [Parameter(Mandatory = $true)]
    [ValidateSet("win-x64", "win-arm64")]
    [string] $Rid
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$Root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$Out = Join-Path $Root "spike\out\$Rid"
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

    function Assert-EqualBytes {
        param(
            [string] $Expected,
            [string] $Actual
        )

        [byte[]] $expectedBytes = [System.IO.File]::ReadAllBytes($Expected)
        [byte[]] $actualBytes = [System.IO.File]::ReadAllBytes($Actual)
        if ($expectedBytes.Length -ne $actualBytes.Length) {
            throw "Unexpected Windows UTF-16 argv output length. Expected $($expectedBytes.Length), got $($actualBytes.Length)."
        }

        for ($index = 0; $index -lt $expectedBytes.Length; $index++) {
            if ($expectedBytes[$index] -ne $actualBytes[$index]) {
                throw "Unexpected Windows UTF-16 argv byte at offset $index. Expected $($expectedBytes[$index]), got $($actualBytes[$index])."
            }
        }
    }

    Require-Tool "cl.exe"
    Require-Tool "link.exe"

    dotnet publish (Join-Path $Root "spike\Scout.Entry") -r $Rid -c Release -p:NativeLib=Static -o $Out
    New-Item -ItemType Directory -Force -Path $Out | Out-Null

    $DriverObject = Join-Path $Out "scout_wmain.obj"
    & cl.exe /nologo /O2 /TC /c (Join-Path $Root "spike\native\scout_wmain.c") "/Fo$DriverObject"
    if ($LASTEXITCODE -ne 0) {
        throw "cl.exe failed for scout_wmain.c."
    }

    $Libraries = [System.Collections.Generic.List[string]]::new()
    $Libraries.Add((Require-Path (Join-Path $Out "Scout.Entry.lib")))
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

    $OutputExe = Join-Path $Out "scout-spike.exe"
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

    & link.exe /NOLOGO /MANIFEST:NO /SUBSYSTEM:CONSOLE /ENTRY:wmainCRTStartup /INCREMENTAL:NO /MERGE:.managedcode=.text /MERGE:hydrated=.bss /OPT:REF /OPT:ICF /NODEFAULTLIB:libucrt.lib /DEFAULTLIB:ucrt.lib "/OUT:$OutputExe" $DriverObject @Libraries @SdkLibraries
    if ($LASTEXITCODE -ne 0) {
        throw "link.exe failed for $OutputExe."
    }

    $Expected = Join-Path $Out "expected.utf16le"
    $Actual = Join-Path $Out "got.utf16le"
    $Argument = "caf$([char]0x00E9)"
    [System.IO.File]::WriteAllBytes($Expected, [System.Text.Encoding]::Unicode.GetBytes("$Argument`n"))

    $StartInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $StartInfo.FileName = $OutputExe
    $StartInfo.ArgumentList.Add($Argument)
    $StartInfo.RedirectStandardOutput = $true
    $Process = [System.Diagnostics.Process]::Start($StartInfo)
    if ($null -eq $Process) {
        throw "Failed to start $OutputExe."
    }

    try {
        $Output = [System.IO.File]::Create($Actual)
        try {
            $Process.StandardOutput.BaseStream.CopyTo($Output)
        }
        finally {
            $Output.Dispose()
        }

        $Process.WaitForExit()
        if ($Process.ExitCode -ne 0) {
            throw "$OutputExe failed with exit code $($Process.ExitCode)."
        }
    }
    finally {
        $Process.Dispose()
    }

    Assert-EqualBytes $Expected $Actual
    Write-Host "OK ${Rid}: UTF-16 argv round-trip"
}
finally {
    Pop-Location
}
