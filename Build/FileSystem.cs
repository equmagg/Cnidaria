using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Cnidaria;

public static class BuildPath
{
    public static string Normalize(string path, string? relativeTo = null)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Length > 2048 || path.IndexOf('\0') >= 0)
            throw new BuildException("Invalid or excessively long virtual path.");
        path = path.Replace('\\', '/');
        if (path.StartsWith("//", StringComparison.Ordinal) || path.IndexOf(':') >= 0)
            throw new BuildException("Device, drive and network paths are not supported.");
        if (path[0] != '/')
            path = (relativeTo is null ? "/" : Normalize(relativeTo)).TrimEnd('/') + "/" + path;
        if (path.Length > 2048)
            throw new BuildException("Virtual path is too long.");
        var parts = path.Split('/');
        var stack = new List<string>(parts.Length);
        foreach (var part in parts)
        {
            if (part.Length == 0 || part == ".") continue;
            if (part == "..")
            {
                if (stack.Count == 0) throw new BuildException("Path escapes the virtual root.");
                stack.RemoveAt(stack.Count - 1);
            }
            else
            {
                if (part.Length > 255 || stack.Count >= 128)
                    throw new BuildException("Virtual path exceeds component limits.");
                stack.Add(part);
            }
        }
        return "/" + string.Join("/", stack);
    }

    public static string Directory(string path)
    {
        path = Normalize(path);
        int slash = path.LastIndexOf('/');
        return slash == 0 ? "/" : path.Substring(0, slash);
    }
}

public interface IFileSystem
{
    bool Exists(string path);
    byte[] Read(string path, int maximumBytes);
    void Write(string path, ReadOnlySpan<byte> data);
    IEnumerable<string> EnumerateFiles(string directory, int maximumEntries);
}

public sealed class InMemoryFileSystem : IFileSystem
{
    private readonly Dictionary<string, byte[]> _files = new(StringComparer.Ordinal);
    private readonly long _maximumBytes;
    private readonly int _maximumFiles;
    private long _storedBytes;

    public InMemoryFileSystem(long maximumBytes = 128L * 1024 * 1024, int maximumFiles = 8192)
    {
        if (maximumBytes < 0 || maximumFiles < 1) throw new ArgumentOutOfRangeException(nameof(maximumBytes));
        _maximumBytes = maximumBytes;
        _maximumFiles = maximumFiles;
    }

    public long StoredBytes => _storedBytes;
    public IEnumerable<string> Paths => _files.Keys;
    public bool Exists(string path) => _files.ContainsKey(BuildPath.Normalize(path));
    public IEnumerable<string> EnumerateFiles(string directory, int maximumEntries)
    {
        if (maximumEntries < 1) throw new ArgumentOutOfRangeException(nameof(maximumEntries));
        int visits = 0;
        var prefix = BuildPath.Normalize(directory).TrimEnd('/') + "/";
        foreach (var path in _files.Keys)
        {
            if (++visits > maximumEntries) throw new BuildException("Filesystem enumeration budget exceeded.");
            if (path.StartsWith(prefix, StringComparison.Ordinal)) yield return path;
        }
    }

    public byte[] Read(string path, int maximumBytes)
    {
        if (maximumBytes < 0) throw new ArgumentOutOfRangeException(nameof(maximumBytes));
        path = BuildPath.Normalize(path);
        if (!_files.TryGetValue(path, out var bytes)) throw new FileNotFoundException("Virtual file not found.", path);
        if (bytes.Length > maximumBytes) throw new BuildException($"Virtual file '{path}' exceeds the read budget.");
        return (byte[])bytes.Clone();
    }

    public void Write(string path, ReadOnlySpan<byte> data)
    {
        path = BuildPath.Normalize(path);
        long oldSize = _files.TryGetValue(path, out var old) ? old.Length : 0;
        if (data.Length > _maximumBytes - (_storedBytes - oldSize) ||
            (old is null && _files.Count >= _maximumFiles))
            throw new BuildException("In-memory filesystem capacity exceeded.");
        _files[path] = data.ToArray();
        _storedBytes = _storedBytes - oldSize + data.Length;
    }
}

public sealed class DirectoryFileSystem : IFileSystem
{
    private readonly string _root;
    public DirectoryFileSystem(string root)
    {
        _root = Path.GetFullPath(root ?? throw new ArgumentNullException(nameof(root)));
        if (!System.IO.Directory.Exists(_root)) throw new DirectoryNotFoundException(_root);
    }

    private string Map(string path)
    {
        path = BuildPath.Normalize(path);
        string mapped = Path.GetFullPath(Path.Combine(_root, path.TrimStart('/').Replace('/', Path.DirectorySeparatorChar)));
        // Reject junctions/symlinks including intermediate directories
        var current = _root;
        if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
            throw new BuildException("Disk root must not be a symbolic link.");
        foreach (var segment in path.TrimStart('/').Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            if ((File.Exists(current) || System.IO.Directory.Exists(current)) &&
                (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new BuildException("Symbolic links in disk-backed paths are forbidden.");
        }
        return mapped;
    }

    public bool Exists(string path) => File.Exists(Map(path));
    public IEnumerable<string> EnumerateFiles(string directory, int maximumEntries)
    {
        if (maximumEntries < 1) throw new ArgumentOutOfRangeException(nameof(maximumEntries));
        int visits = 0;
        var root = BuildPath.Normalize(directory);
        string mapped = Map(root);
        if (!System.IO.Directory.Exists(mapped)) yield break;
        var pending = new Stack<(string Virtual, string Physical)>();
        pending.Push((root, mapped));
        while (pending.Count != 0)
        {
            var current = pending.Pop();
            foreach (var entry in System.IO.Directory.EnumerateFileSystemEntries(current.Physical))
            {
                if (++visits > maximumEntries) throw new BuildException("Filesystem enumeration budget exceeded.");
                var name = Path.GetFileName(entry);
                var virtualPath = BuildPath.Normalize(name, current.Virtual);
                var attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                    continue; // Never enumerate through links or junctions
                if ((attributes & FileAttributes.Directory) != 0)
                    pending.Push((virtualPath, entry));
                else if ((attributes & FileAttributes.Normal) != 0 || File.Exists(entry))
                    yield return virtualPath;
            }
        }
    }
    public byte[] Read(string path, int maximumBytes)
    {
        if (maximumBytes < 0) throw new ArgumentOutOfRangeException(nameof(maximumBytes));
        using var stream = new FileStream(Map(path), FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > maximumBytes) throw new BuildException("Disk-backed file exceeds the read budget.");
        var bytes = GC.AllocateUninitializedArray<byte>((int)stream.Length);
        stream.ReadExactly(bytes);
        if (stream.ReadByte() != -1)
            throw new BuildException("Disk-backed file changed while being read.");
        return bytes;
    }
    public void Write(string path, ReadOnlySpan<byte> data)
    {
        var mapped = Map(path);
        System.IO.Directory.CreateDirectory(Path.GetDirectoryName(mapped)!);
        // Recheck after directory creation
        mapped = Map(path);
        using var stream = new FileStream(mapped, FileMode.Create, FileAccess.Write, FileShare.None);
        stream.Write(data);
    }
}

public sealed class OverlayFileSystem : IFileSystem
{
    private readonly IFileSystem _inputs;
    private readonly InMemoryFileSystem _outputs;
    public OverlayFileSystem(IFileSystem inputs, long maximumRamBytes = 128L * 1024 * 1024, int maximumRamFiles = 8192)
    {
        _inputs = inputs ?? throw new ArgumentNullException(nameof(inputs));
        _outputs = new InMemoryFileSystem(maximumRamBytes, maximumRamFiles);
    }

    public IEnumerable<string> PendingPaths => _outputs.Paths;
    public long PendingBytes => _outputs.StoredBytes;
    public bool Exists(string path) => _outputs.Exists(path) || _inputs.Exists(path);
    public byte[] Read(string path, int maximumBytes) => _outputs.Exists(path) ? _outputs.Read(path, maximumBytes) : _inputs.Read(path, maximumBytes);
    public void Write(string path, ReadOnlySpan<byte> data) => _outputs.Write(path, data);

    public IEnumerable<string> EnumerateFiles(string directory, int maximumEntries)
    {
        if (maximumEntries < 1) throw new ArgumentOutOfRangeException(nameof(maximumEntries));
        int visible = 0;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var path in _outputs.EnumerateFiles(directory, maximumEntries))
        {
            seen.Add(path);
            if (++visible > maximumEntries)
                throw new BuildException("Overlay enumeration budget exceeded.");
            yield return path;
        }
        foreach (var path in _inputs.EnumerateFiles(directory, maximumEntries))
        {
            string normalized = BuildPath.Normalize(path);
            if (!seen.Add(normalized)) continue;
            if (++visible > maximumEntries) throw new BuildException("Overlay enumeration budget exceeded.");
            yield return normalized;
        }
    }

    public void FlushTo(IFileSystem destination, IEnumerable<string> selectedPaths, long maximumBytes = 128L * 1024 * 1024, int maximumFiles = 8192)
    {
        if (destination is null || selectedPaths is null) throw new ArgumentNullException(destination is null ? nameof(destination) : nameof(selectedPaths));
        if (maximumBytes < 1 || maximumFiles < 1) throw new ArgumentOutOfRangeException(nameof(maximumBytes));
        long written = 0;
        int count = 0;
        foreach (var candidate in selectedPaths)
        {
            if (++count > maximumFiles) throw new BuildException("Overlay flush file budget exceeded.");
            string path = BuildPath.Normalize(candidate);
            if (!_outputs.Exists(path)) throw new FileNotFoundException("Path is not a pending overlay output.", path);
            int allowance = (int)Math.Min(int.MaxValue, maximumBytes - written);
            if (allowance < 0) throw new BuildException("Overlay flush size budget exceeded.");
            var bytes = _outputs.Read(path, allowance);
            written += bytes.LongLength;
            destination.Write(path, bytes);
        }
    }
}

public sealed class BuildException : Exception
{
    public BuildException(string message) : base(message) { }
    public BuildException(string message, Exception inner) : base(message, inner) { }
}
