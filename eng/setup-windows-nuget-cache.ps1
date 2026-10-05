Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if (-not $IsWindows) {
    throw "Windows NuGet cache setup must run on Windows."
}

foreach ($name in @("RUNNER_TEMP", "GITHUB_ENV")) {
    if ([string]::IsNullOrWhiteSpace([Environment]::GetEnvironmentVariable($name))) {
        throw "$name is required."
    }
}

# Keep restored packages outside the checkout, on the runner's build drive.
$cacheRoot = Join-Path $env:RUNNER_TEMP "scout-nuget"
$paths = [ordered]@{
    NUGET_PACKAGES = Join-Path $cacheRoot "packages"
    NUGET_HTTP_CACHE_PATH = Join-Path $cacheRoot "http-cache"
}

foreach ($entry in $paths.GetEnumerator()) {
    New-Item -ItemType Directory -Path $entry.Value -Force | Out-Null
    Add-Content -LiteralPath $env:GITHUB_ENV -Value "$($entry.Key)=$($entry.Value)" -Encoding utf8
    [Environment]::SetEnvironmentVariable($entry.Key, $entry.Value)
    Write-Host "$($entry.Key)=$($entry.Value)"
}
