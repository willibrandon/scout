namespace Scout;

/// <summary>
/// Verifies the Native AOT Windows build exercises redirected standard input through an anonymous pipe.
/// </summary>
public sealed class NativeStandardInputConfigurationTests
{
    /// <summary>
    /// Verifies both native Windows build modes run matching and nonmatching pipe smoke cases.
    /// </summary>
    [Fact]
    public void WindowsNativeBuildRunsAnonymousPipeStandardInputSmoke()
    {
        string root = FindRepositoryRoot();
        string buildScript = File.ReadAllText(Path.Combine(root, "native", "build-app-windows.ps1"));
        string smokeScript = File.ReadAllText(Path.Combine(root, "native", "test-standard-input-windows.ps1"));

        Assert.Contains("dotnet publish failed for $Rid", buildScript, StringComparison.Ordinal);
        Assert.Contains("native\\test-standard-input-windows.ps1", buildScript, StringComparison.Ordinal);
        Assert.Contains("RedirectStandardInput = $true", smokeScript, StringComparison.Ordinal);
        Assert.Contains("StandardInput.BaseStream.Write", smokeScript, StringComparison.Ordinal);
        Assert.Contains("StandardInput.Close()", smokeScript, StringComparison.Ordinal);
        Assert.Contains("Assert-MatchingStandardInput $false", smokeScript, StringComparison.Ordinal);
        Assert.Contains("Assert-MatchingStandardInput $true", smokeScript, StringComparison.Ordinal);
        Assert.Contains("Assert-NonmatchingStandardInput $false", smokeScript, StringComparison.Ordinal);
        Assert.Contains("Assert-NonmatchingStandardInput $true", smokeScript, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        string? path = AppContext.BaseDirectory;
        while (path is not null && !File.Exists(Path.Combine(path, "Scout.slnx")))
        {
            path = Directory.GetParent(path)?.FullName;
        }

        return path ?? throw new InvalidOperationException("Could not locate the Scout repository root.");
    }
}
