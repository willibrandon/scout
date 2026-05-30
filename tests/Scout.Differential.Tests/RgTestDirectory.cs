using System;
using System.IO;

namespace Scout;

internal sealed class RgTestDirectory : IDisposable
{
    private RgTestDirectory(string path)
    {
        RootPath = path;
    }

    public string RootPath { get; }

    public static RgTestDirectory Create(string name)
    {
        string path = Path.Combine(Path.GetTempPath(), "scout-rgtest-" + name + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return new RgTestDirectory(path);
    }

    public RgTestDirectory Clone(string suffix)
    {
        string path = Path.Combine(Path.GetTempPath(), "scout-rgtest-" + suffix + "-" + Guid.NewGuid().ToString("N"));
        CopyDirectory(RootPath, path);
        return new RgTestDirectory(path);
    }

    public void CreateFile(string relativePath, string contents)
    {
        CreateBytes(relativePath, EncodingUtf8.GetBytes(contents));
    }

    public void SetLastAccessTimeUtc(string relativePath, DateTime lastAccessTimeUtc)
    {
        File.SetLastAccessTimeUtc(Path.Combine(RootPath, relativePath), lastAccessTimeUtc);
    }

    public void CreateBytes(string relativePath, byte[] contents)
    {
        string path = Path.Combine(RootPath, relativePath);
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllBytes(path, contents);
    }

    public void CreateSize(string relativePath, long size)
    {
        string path = Path.Combine(RootPath, relativePath);
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using FileStream stream = File.Create(path);
        stream.SetLength(size);
    }

    public void CreateDirectory(string relativePath)
    {
        Directory.CreateDirectory(Path.Combine(RootPath, relativePath));
    }

    public void Dispose()
    {
        Directory.Delete(RootPath, recursive: true);
    }

    private static void CopyDirectory(string sourcePath, string destinationPath)
    {
        Directory.CreateDirectory(destinationPath);
        CopyDirectoryTimes(sourcePath, destinationPath);
        foreach (string directory in Directory.EnumerateDirectories(sourcePath))
        {
            string childDestination = Path.Combine(destinationPath, Path.GetFileName(directory));
            CopyDirectory(directory, childDestination);
        }

        foreach (string file in Directory.EnumerateFiles(sourcePath))
        {
            string childDestination = Path.Combine(destinationPath, Path.GetFileName(file));
            File.Copy(file, childDestination);
            CopyFileTimes(file, childDestination);
        }
    }

    private static void CopyDirectoryTimes(string sourcePath, string destinationPath)
    {
        Directory.SetCreationTimeUtc(destinationPath, Directory.GetCreationTimeUtc(sourcePath));
        Directory.SetLastWriteTimeUtc(destinationPath, Directory.GetLastWriteTimeUtc(sourcePath));
        Directory.SetLastAccessTimeUtc(destinationPath, Directory.GetLastAccessTimeUtc(sourcePath));
    }

    private static void CopyFileTimes(string sourcePath, string destinationPath)
    {
        File.SetCreationTimeUtc(destinationPath, File.GetCreationTimeUtc(sourcePath));
        File.SetLastWriteTimeUtc(destinationPath, File.GetLastWriteTimeUtc(sourcePath));
        File.SetLastAccessTimeUtc(destinationPath, File.GetLastAccessTimeUtc(sourcePath));
    }
}
