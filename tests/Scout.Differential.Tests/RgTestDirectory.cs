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

    public void CreateFile(string relativePath, string contents)
    {
        CreateBytes(relativePath, EncodingUtf8.GetBytes(contents));
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
}
