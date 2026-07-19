param(
    [Parameter(Mandatory = $true)]
    [string] $ScoutPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Invoke-ScoutWithStandardInput {
    param(
        [string[]] $Arguments,
        [byte[]] $InputBytes
    )

    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $ScoutPath
    $startInfo.CreateNoWindow = $true
    $startInfo.RedirectStandardError = $true
    $startInfo.RedirectStandardInput = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.UseShellExecute = $false
    $null = $startInfo.Environment.Remove("RIPGREP_CONFIG_PATH")
    $null = $startInfo.Environment.Remove("SCOUT_CONFIG_PATH")
    foreach ($argument in $Arguments) {
        $null = $startInfo.ArgumentList.Add($argument)
    }

    $process = [System.Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    $output = [System.IO.MemoryStream]::new()
    try {
        if (-not $process.Start()) {
            throw "Failed to start $ScoutPath."
        }

        $outputTask = $process.StandardOutput.BaseStream.CopyToAsync($output)
        $errorTask = $process.StandardError.ReadToEndAsync()
        $process.StandardInput.BaseStream.Write($InputBytes, 0, $InputBytes.Length)
        $process.StandardInput.Close()
        if (-not $process.WaitForExit(30000)) {
            $process.Kill($true)
            throw "Scout redirected-standard-input smoke timed out."
        }

        $null = $outputTask.GetAwaiter().GetResult()
        $errorText = $errorTask.GetAwaiter().GetResult()
        return [pscustomobject] @{
            ExitCode = $process.ExitCode
            Output = [System.Text.Encoding]::UTF8.GetString($output.ToArray())
            Error = $errorText
        }
    }
    finally {
        $output.Dispose()
        $process.Dispose()
    }
}

function Assert-MatchingStandardInput {
    param([bool] $ExplicitStandardInput)

    $arguments = [System.Collections.Generic.List[string]]::new()
    $null = $arguments.Add("--no-config")
    $null = $arguments.Add("-n")
    $null = $arguments.Add("--stats")
    $null = $arguments.Add("Scout")
    if ($ExplicitStandardInput) {
        $null = $arguments.Add("-")
    }

    $inputBytes = [System.Text.Encoding]::ASCII.GetBytes("ScoutScout`r`n")
    $result = Invoke-ScoutWithStandardInput $arguments.ToArray() $inputBytes
    if ($result.ExitCode -ne 0 -or $result.Error -ne "") {
        throw "Matching redirected-standard-input smoke failed: exit=$($result.ExitCode) stderr=$($result.Error)"
    }

    if (-not $result.Output.StartsWith("1:ScoutScout`r`n`n", [System.StringComparison]::Ordinal) -or
        -not $result.Output.Contains("2 matches`n", [System.StringComparison]::Ordinal) -or
        -not $result.Output.Contains("1 matched lines`n", [System.StringComparison]::Ordinal) -or
        -not $result.Output.Contains("12 bytes searched`n", [System.StringComparison]::Ordinal)) {
        throw "Unexpected matching redirected-standard-input output: $($result.Output)"
    }
}

function Assert-NonmatchingStandardInput {
    param([bool] $ExplicitStandardInput)

    $arguments = [System.Collections.Generic.List[string]]::new()
    $null = $arguments.Add("--no-config")
    $null = $arguments.Add("--stats")
    $null = $arguments.Add("Scout")
    if ($ExplicitStandardInput) {
        $null = $arguments.Add("-")
    }

    $inputBytes = [System.Text.Encoding]::ASCII.GetBytes("NoMatch`r`n")
    $result = Invoke-ScoutWithStandardInput $arguments.ToArray() $inputBytes
    if ($result.ExitCode -ne 1 -or $result.Error -ne "") {
        throw "Nonmatching redirected-standard-input smoke failed: exit=$($result.ExitCode) stderr=$($result.Error)"
    }

    if (-not $result.Output.StartsWith("`n0 matches`n", [System.StringComparison]::Ordinal) -or
        -not $result.Output.Contains("0 matched lines`n", [System.StringComparison]::Ordinal) -or
        -not $result.Output.Contains("9 bytes searched`n", [System.StringComparison]::Ordinal)) {
        throw "Unexpected nonmatching redirected-standard-input output: $($result.Output)"
    }
}

Assert-MatchingStandardInput $false
Assert-MatchingStandardInput $true
Assert-NonmatchingStandardInput $false
Assert-NonmatchingStandardInput $true
Write-Host "OK: Windows anonymous-pipe standard input"
