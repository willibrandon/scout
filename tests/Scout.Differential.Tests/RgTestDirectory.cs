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
        CopyDirectory(RootPath, path, RootPath, path);
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

    public void LinkDirectory(string sourceRelativePath, string targetRelativePath)
    {
        string sourcePath = Path.GetFullPath(Path.Combine(RootPath, sourceRelativePath));
        string targetPath = Path.GetFullPath(Path.Combine(RootPath, targetRelativePath));
        string? targetDirectory = Path.GetDirectoryName(targetPath);
        if (!string.IsNullOrEmpty(targetDirectory))
        {
            Directory.CreateDirectory(targetDirectory);
        }

        Directory.CreateSymbolicLink(targetPath, sourcePath);
    }

    public void LinkFile(string sourceRelativePath, string targetRelativePath)
    {
        string sourcePath = Path.GetFullPath(Path.Combine(RootPath, sourceRelativePath));
        string targetPath = Path.GetFullPath(Path.Combine(RootPath, targetRelativePath));
        string? targetDirectory = Path.GetDirectoryName(targetPath);
        if (!string.IsNullOrEmpty(targetDirectory))
        {
            Directory.CreateDirectory(targetDirectory);
        }

        File.CreateSymbolicLink(targetPath, sourcePath);
    }

    public void Dispose()
    {
        Directory.Delete(RootPath, recursive: true);
    }

    private static void CopyDirectory(string sourcePath, string destinationPath, string sourceRootPath, string destinationRootPath)
    {
        Directory.CreateDirectory(destinationPath);
        foreach (FileSystemInfo entry in new DirectoryInfo(sourcePath).EnumerateFileSystemInfos())
        {
            string childDestination = Path.Combine(destinationPath, entry.Name);
            if (entry.LinkTarget is string linkTarget)
            {
                CopySymbolicLink(entry, childDestination, RewriteLinkTarget(linkTarget, sourceRootPath, destinationRootPath));
                continue;
            }

            switch (entry)
            {
                case DirectoryInfo directory:
                    CopyDirectory(directory.FullName, childDestination, sourceRootPath, destinationRootPath);
                    break;

                case FileInfo file:
                    File.Copy(file.FullName, childDestination);
                    CopyFileTimes(file.FullName, childDestination);
                    break;
            }
        }

        CopyDirectoryTimes(sourcePath, destinationPath);
    }

    private static void CopySymbolicLink(FileSystemInfo source, string destinationPath, string linkTarget)
    {
        if ((source.Attributes & FileAttributes.Directory) != 0)
        {
            Directory.CreateSymbolicLink(destinationPath, linkTarget);
            return;
        }

        File.CreateSymbolicLink(destinationPath, linkTarget);
    }

    private static string RewriteLinkTarget(string linkTarget, string sourceRootPath, string destinationRootPath)
    {
        if (!Path.IsPathFullyQualified(linkTarget))
        {
            return linkTarget;
        }

        string targetPath = Path.GetFullPath(linkTarget);
        string sourceRoot = Path.GetFullPath(sourceRootPath);
        if (!IsPathWithin(targetPath, sourceRoot))
        {
            return linkTarget;
        }

        string relativeTarget = Path.GetRelativePath(sourceRoot, targetPath);
        return Path.Combine(Path.GetFullPath(destinationRootPath), relativeTarget);
    }

    private static bool IsPathWithin(string path, string root)
    {
        string relativePath = Path.GetRelativePath(root, path);
        return relativePath == "."
            || (!relativePath.StartsWith("..", StringComparison.Ordinal) && !Path.IsPathRooted(relativePath));
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
